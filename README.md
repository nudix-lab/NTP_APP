# NTP Timer

NTP 서버에서 시각을 받아와 **밀리초(ms) 단위**까지 표시하는 Windows 데스크톱 시계.
검정 배경에 밝은 글자로 날짜와 `HH:mm:ss.fff` 를 크게 보여준다.

- **.NET 8 / WPF** 단일 창 앱
- 시작 시 NTP **1회** 동기화 → 이후 표시는 고해상도 `Stopwatch` 로 흘러가므로 부드럽고 서버 부담이 없다
- 왕복지연(round-trip delay)을 SNTP 4-타임스탬프 공식으로 보정

## 요구 사항

- Windows
- 실행만: **.NET 8 Desktop Runtime**
- 빌드까지: **.NET 8 SDK 이상** (현재 9.0.x SDK 에서도 `net8.0-windows` 타겟 빌드 가능)
- 인터넷(아웃바운드 **UDP 123**) — 동기화 시점에만 필요. 막혀 있으면 로컬 시계로 폴백 표시

## 빌드

```powershell
dotnet build NtpTimer.csproj -c Release
```

## 실행

개발 중(소스에서 바로):

```powershell
dotnet run --project .
```

빌드된 실행 파일로:

```powershell
.\bin\Release\net8.0-windows\NtpTimer.exe
```

## 사용법 / 화면

- **상단**: 날짜 `yyyy.MM.dd (요일)`
- **중앙**: `HH:mm:ss.fff` — 창 크기에 맞춰 자동 확대
- **하단 상태줄**: 동기화에 사용한 서버 · 오프셋(로컬시계 대비) · 왕복지연
- **UTC 표시** 체크박스: 로컬시간 ↔ UTC 전환 (기본은 로컬시간)
- **재동기화** 버튼: NTP 를 다시 1회 질의해 앵커를 갱신

## NTP 동작 방식

1. 시작 시 아래 서버를 순서대로 질의해 **첫 성공** 응답을 사용
   `time.google.com` → `time.cloudflare.com` → `pool.ntp.org` → `time.windows.com`
2. 송신 직전(t1)·수신 직후(t4)의 로컬 UTC 와 `Stopwatch` 를 기록하고, 서버의 수신(t2)·송신(t3) 타임스탬프로
   `offset = ((t2-t1)+(t3-t4))/2` 를 계산
3. 수신 순간의 "참 UTC = t4 + offset" 을 `Stopwatch` 틱에 앵커링
4. 이후 표시값 = `앵커UTC + (현재 Stopwatch - 앵커 Stopwatch)`

서버 목록·갱신 주기(16ms)는 [MainWindow.xaml.cs](MainWindow.xaml.cs) 상단에서 변경할 수 있다.

## 정확도 한계

WAN NTP 는 네트워크 왕복지연과 OS 스케줄링 때문에 절대 정확도가 보통 **수~수십 ms** 수준이다
(왕복지연 보정은 적용됨). ms **자릿수**는 매끄럽게 표시되지만 "참 UTC 와 ms 까지 완벽 일치"를 보장하지는 않는다.

## 구성

| 파일 | 역할 |
|---|---|
| `NtpClient.cs` | SNTP(v3) UDP 클라이언트. 왕복지연 보정 후 UTC↔Stopwatch 앵커 산출 |
| `MainWindow.xaml` / `.xaml.cs` | 검정 배경 GUI + 표시 갱신 루프 |
| `App.xaml` / `.xaml.cs` | WPF 앱 진입점 |
