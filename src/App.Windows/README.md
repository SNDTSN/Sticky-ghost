# App.Windows

Windows용 실행 진입점(WinExe). `App.UI`(뷰/뷰모델, 플랫폼 무관)와 `App.Platform.Windows`(DPAPI 등 Win32 종속 구현)를 둘 다 참조하는 유일한 프로젝트 — 구체 플랫폼 구현을 실제로 `new`해서 `App.UI.App`의 델리게이트(`SecretStoreFactory` 등)에 꽂아주는 조립 루트.

Mac 이식 시 이 프로젝트는 그대로 두고, `App.Platform.Mac` + `App.Mac`(같은 역할의 새 진입점)을 추가하면 된다.
