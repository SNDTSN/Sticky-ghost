using System.Runtime.InteropServices;
using App.Core.Diagnostics;
using App.Core.Infrastructure.Ipc;

namespace App.Windows;

/// <summary>
/// 앱이 두 번 실행되는 것을 막고, 두 번째 실행은 이미 떠 있는 인스턴스의 메인 창을 앞으로 가져온 뒤 조용히 종료하게 한다.
/// 이중 실행을 막는 이유: 두 인스턴스가 같은 SQLite/설정 파일과 IPC 파이프(최대 1개)를 두고 다투는데,
/// 파이프 생성이 실패하면 한때 UI 스레드가 무한 루프에 빠졌다(docs/stability-hardening.md).
/// 뮤텍스와 포그라운드 권한 부여는 Windows API라 실행 진입점(App.Windows) 소관이다 — Mac 이식 시 App.Mac이 자기 방식으로 구현.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    // Local\ 접두사: 같은 로그인 세션 안에서만 단일 인스턴스로 본다(다른 사용자 세션의 앱과는 무관).
    private const string MutexName = @"Local\StickyGhost.SingleInstance";
    private const uint AsfwAny = unchecked((uint)-1);

    private readonly Mutex _mutex;

    public bool IsFirstInstance { get; }

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
    }

    /// <summary>
    /// 이미 실행 중인 인스턴스에게 메인 창을 앞으로 가져오라고 요청한다. 실패해도(기존 인스턴스가 아직 기동 중이라
    /// 파이프가 없는 경우 등) 예외를 던지지 않고 로그만 남긴다 — 어차피 이 프로세스는 곧 종료된다.
    /// </summary>
    public void NotifyExistingInstance()
    {
        try
        {
            // Windows는 포그라운드 권한이 없는 프로세스가 창을 앞으로 올리는 것을 막는다. 방금 사용자가 실행해서
            // 포그라운드 권한을 가진 이 프로세스가 그 권한을 넘겨줘야 기존 인스턴스의 Activate()가 실제로 먹힌다.
            AllowSetForegroundWindow(AsfwAny);

            var response = Task.Run(() => new CharacterIpcClient().SendAsync(
                new CharacterIpcRequest(CharacterIpcRequest.TypeActivate, null, null), CancellationToken.None))
                .GetAwaiter().GetResult();

            if (!response.IsSuccess)
                AppLog.Write("single-instance", $"기존 인스턴스 활성화 요청 실패: {response.ErrorMessage}");
        }
        catch (Exception ex)
        {
            AppLog.Write("single-instance", ex);
        }
    }

    public void Dispose()
    {
        if (IsFirstInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 다른 스레드에서 Dispose된 경우 — 프로세스 종료 시 OS가 어차피 정리한다.
            }
        }

        _mutex.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);
}
