namespace App.Core.Infrastructure.Ipc;

/// <summary>
/// 명명 파이프로 들어온 요청을 실제로 처리하는 쪽의 인터페이스. App.Core는 파이프 프로토콜(수신/전송)만 알고,
/// "표정을 바꾼다"가 실제로 무엇을 의미하는지(Avalonia 창 조작 등)는 App.UI가 구현해서 주입한다.
/// </summary>
public interface ICharacterIpcRequestHandler
{
    CharacterIpcResponse Handle(CharacterIpcRequest request);
}
