# HALILL

Google Calendar 일정을 보여 주는 Windows 데스크톱 캘린더 위젯입니다. C# / .NET 8 / WPF로 구현했습니다.

## 현재 기능

- 한국어 월간 달력, 오늘 이동, 날짜별 일정과 장소 표시
- 날짜마다 일정 제목을 가로 바 최대 3개로 미리보기, 초과 일정은 날짜 숫자 오른쪽에 `+N개`로 표시
- 종일 일정은 초록색, 시간 일정은 파란색으로 구분; 미리보기 제목은 최대 6글자, 전체 제목은 툴팁 제공
- 날짜를 선택하면 달력 아래에 해당 날짜의 전체 일정 표시 (기본 창 크기 680 × 980, 상세 일정 3개가 보이는 높이)
- 고정·최소화·닫기는 상단 오른쪽에 배치; 불필요한 마지막 주는 앱 배경색과 동일한 클릭할 수 없는 영역으로 표시
- 창 드래그, 크기 조절, 항상 위에 표시, 창 위치 저장
- Google 브라우저 로그인: Authorization Code + PKCE + loopback callback
- 읽기 전용 Calendar API, 캘린더 선택, 반복 일정 전개, 페이지 처리
- 종일/여러 날 일정 및 PC 현지 시간대 변환
- 수동 새로고침, 5분 자동 갱신, 토큰 자동 갱신
- Windows DPAPI로 OAuth 설정과 토큰 암호화 저장
- 연결 전에는 명확히 표시된 샘플 일정 제공

이 버전은 **독립적인 데스크톱 창**입니다. Windows 11의 Win+W 위젯 보드 등록, 시스템 트레이, Windows 시작 시 자동 실행, 일정 편집은 구현 범위에 포함하지 않았습니다. 한 번에 선택한 캘린더 하나를 표시합니다.

## 실행

Windows 10/11에서 .NET 8 SDK를 설치한 뒤 저장소의 `run.cmd`를 실행합니다. 프로젝트 내부 `.tools/dotnet`에 SDK가 있으면 우선 사용합니다.

```powershell
dotnet run --project src/Halill/Halill.csproj
```

최신 브라우저 로그인 버전은 `artifacts/browser-login/Halill.exe`로 실행할 수 있습니다. 프레임워크 종속 배포이므로 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)이 필요합니다.

## Google Calendar 연결 (사용자)

1. **Google 캘린더 연결**을 누릅니다.
2. 기본 웹브라우저에서 Google 계정으로 로그인하고 조회 권한을 승인합니다.
3. 브라우저가 인증 코드를 앱의 로컬 콜백으로 전달합니다. 앱은 PKCE 검증을 포함해 토큰으로 교환하고 일정을 표시합니다.

사용자는 OAuth JSON 파일을 다운로드하거나 선택하지 않습니다. 로그인은 최대 3분 동안 기다리며, **로그인 취소** 버튼이나 앱 종료로 대기를 끝낼 수 있습니다.

현재 저장소에는 실제 Google OAuth 앱 등록 값이 포함되어 있지 않습니다. 개발자가 아래 설정을 완료하기 전에는 연결 버튼에 로그인 설정 미등록 안내가 표시됩니다. 실제 Google 로그인과 일정 조회는 아직 통합 검증하지 않았습니다.

## Google 앱 등록 (개발자 최초 1회)

1. [Google Cloud Console](https://console.cloud.google.com/)에서 프로젝트를 생성하거나 선택합니다.
2. API 라이브러리에서 **Google Calendar API**를 활성화합니다.
3. Google Auth Platform에서 앱 이름, 지원 이메일, 대상 사용자를 설정합니다. 외부 사용자용 테스트 앱이면 로그인할 Google 계정을 테스트 사용자에 추가합니다.
4. OAuth 클라이언트를 만들 때 유형을 **데스크톱 앱**으로 선택합니다. 웹 애플리케이션용 클라이언트는 사용하지 않습니다.
5. `src/Halill/GoogleOAuthClient.example.xml`을 같은 폴더의 `GoogleOAuthClient.local.xml`로 복사합니다.
6. 등록한 클라이언트의 ID와 시크릿을 각각 `ClientId`, `ClientSecret`에 입력합니다.
7. 앱을 빌드/배포합니다. 해당 등록 값은 앱 리소스에 포함되므로 사용자는 별도 파일이나 개발자 설정을 준비할 필요가 없습니다. 로컬 설정 파일은 Git에서 제외됩니다.

개발 중에는 프로세스 환경 변수 `HALILL_GOOGLE_CLIENT_ID`, `HALILL_GOOGLE_CLIENT_SECRET`으로 등록 값을 지정할 수도 있습니다. 환경 변수 ID가 있으면 두 값 모두 환경 변수에서 읽습니다. 데스크톱 앱에 포함된 클라이언트 시크릿은 추출 가능하므로 서버용 비밀로 취급하지 않습니다. 사용자별 토큰은 Windows DPAPI로 별도 암호화합니다.

요청하는 OAuth 범위:

```text
https://www.googleapis.com/auth/calendar.events.readonly
https://www.googleapis.com/auth/calendar.calendarlist.readonly
```

외부 사용자용 앱이 테스트 상태이면 Google 정책에 따라 refresh token이 만료될 수 있습니다. 인증 갱신 안내가 나오면 다시 연결하세요. 공개 배포 전에는 Google OAuth 게시/검증 요구 사항을 확인해야 합니다.

## 데이터 저장과 해제

`%LOCALAPPDATA%/HALILL`에 창 위치와 선택한 캘린더 설정(`settings.json`), 암호화한 인증 정보(`client.bin`, `tokens.bin`)를 저장합니다. 일정 본문은 디스크에 저장하지 않습니다. 인증 파일은 같은 Windows 사용자 계정에서만 복호화할 수 있습니다.

**해제** 버튼은 이 PC의 인증 정보를 삭제하고 샘플 화면으로 돌아갑니다. Google 계정의 앱 접근 권한까지 철회하려면 [Google 계정 연결 관리](https://myaccount.google.com/connections)에서 HALILL용으로 설정한 앱의 연결을 삭제하세요.

네트워크 오류가 발생하면 같은 화면의 기존 조회 결과를 유지합니다. 다른 달이나 다른 캘린더로 이동했을 때는 이전 범위의 일정을 지우고 새 범위를 조회합니다. 앱 재시작 후 오프라인 조회는 지원하지 않습니다.

## 개발 및 검증

외부 NuGet 패키지 없이 .NET 기본 라이브러리를 사용합니다.

```powershell
dotnet build src/Halill/Halill.csproj -c Release
dotnet run --project tests/Halill.Tests/Halill.Tests.csproj
dotnet publish src/Halill/Halill.csproj -c Release -o artifacts/browser-login
```

저장소 내부 SDK를 사용하는 경우 위 명령의 `dotnet`을 `.\.tools\dotnet\dotnet.exe`로 바꿉니다.

날짜 테스트는 종일 일정 종료일 제외, 여러 날 일정, 자정 종료, 길이가 0인 일정, 시간대 변환, 취소 일정, 6주 달력 경계를 검증합니다. 앱 등록 설정 로딩과 누락/잘못된 설정 거부도 검증합니다. 실제 Google OAuth 및 API 통합 검증은 개발자의 앱 등록과 사용자의 브라우저 로그인이 필요합니다.

코드 구조:

- `src/Halill/MainWindow.xaml`: 위젯 화면
- `src/Halill/MainWindow.xaml.cs`: 화면 갱신, 사용자 조작, 자동 새로고침
- `src/Halill/GoogleCalendarService.cs`: Google 인증과 API 조회
- `src/Halill/GoogleOAuthConfiguration.cs`: 개발자 앱 등록 값 로딩 (사용자 파일 선택 없음)
- `src/Halill/LocalStore.cs`: 로컬 설정 및 DPAPI 암호화
- `src/Halill/Models.cs`: 일정 변환, 날짜 겹침 판단, 샘플 데이터
- `tests/Halill.Tests`: 외부 테스트 패키지가 필요 없는 날짜 처리 검증

## 개발 단계

1. 데스크톱 위젯과 샘플 달력 구현
2. Google 인증과 읽기 전용 일정 조회 구현
3. 날짜/시간대, 오류 처리, 인증 보관 검증
4. 개인 Google 프로젝트를 연결해 실제 계정 통합 검증
5. 필요에 따라 트레이, 자동 시작, 설치 패키지, Windows 위젯 보드 지원 확장

참고: [Google 데스크톱 OAuth](https://developers.google.com/identity/protocols/oauth2/native-app), [일정 조회 API](https://developers.google.com/workspace/calendar/api/v3/reference/events/list).
