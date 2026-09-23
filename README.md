# 6 Sol Danmaku

Unity 6으로 만든 작은 2D 탄막 회피 테스트 프로젝트입니다. GPT-6 Sol을 Codex에서 사용해 기능 구현과 디버깅을 반복하는 *vibe coding* 실험, 그 과정의 Codex 사용량·할당량 관찰, Jev API를 이용한 2D 공간 판단 실험을 위해 만들었습니다. 정식 벤치마크나 상용 게임은 아닙니다.

## 바로 실행하기

1. Unity Hub에서 저장소 루트를 **Unity 6000.6.2f1** 프로젝트로 엽니다.
2. `Assets/Scenes/MainMenu.unity`를 열고 Play를 누릅니다. Build Settings에서도 `MainMenu`가 첫 씬이고 `Main`이 다음 씬입니다.
3. **사람 모드**를 누르면 API Key 없이 플레이할 수 있습니다. **봇 모드**와 **THE ALMIGHTY**의 Jev 계획을 사용하려면 아래 [Jev API 설정](#jev-api-설정)을 먼저 완료합니다.

## 개발 환경

| 항목 | 저장소에서 확인한 구성 |
| --- | --- |
| Unity Editor | `6000.6.2f1` (`ProjectSettings/ProjectVersion.txt`) |
| 렌더링·UI | Universal Render Pipeline 2D, Canvas 기반 uGUI |
| 입력 | Unity Input System (`com.unity.inputsystem`), 키보드 |
| 주요 패키지 | URP `17.6.0`, Input System `1.20.0`, uGUI `2.6.0` |
| AI 개발 도구 | 프로젝트 manifest에 `com.unity.ai.assistant`와 `com.unity.pipeline`이 포함됨. 게임 플레이 코드는 Jev 호출에 UnityWebRequest를 사용함 |

메뉴와 일부 버튼은 OS의 `Malgun Gothic` 글꼴을 요청합니다. 다른 OS에서 실행할 경우 글꼴 표시를 확인해 주세요. 개인 컴퓨터의 절대 경로는 프로젝트 설정에 필요하지 않습니다.

## 게임 규칙

- 목표는 탄환을 피하며 **세 번째 라이프를 잃을 때까지** 오래 생존하는 것입니다. 시작 라이프는 3개입니다.
- Human 조작은 **WASD 또는 방향키**입니다. 8방향 이동이 가능하며 대각선 입력을 정규화합니다. **Left Shift**를 누르는 동안 이동 속도가 평소의 40%가 됩니다. `Main` 씬의 기본 일반 속도는 초당 월드 5단위, 저속은 2단위입니다.
- 플레이어 스프라이트가 화면 밖으로 나가지 않도록 카메라 viewport와 스프라이트 크기로 이동 위치를 제한합니다.
- 탄환은 화면 **상단·좌측·우측 바깥**의 임의 위치에서 안쪽으로 들어옵니다. 각 탄환의 속도는 플레이어 저속 이동 속도부터 일반 속도의 2배까지(기본 설정에서 초당 2~10 월드 단위) 정해집니다. 게임 시작 시 탄환 생성률은 초당 2개이며, 60초 동안 선형으로 증가해 초당 20개가 됩니다. 동시에 활성화된 탄환은 최대 **50개**이고, 가득 찼을 때의 생성은 누적하지 않습니다. 탄환 GameObject는 50개를 미리 만들어 재사용합니다.
- 탄환이 플레이어와 충돌하면 탄환이 풀로 돌아가고 라이프 1개를 잃습니다. 한 피격을 처리하는 동안 추가 차감을 막습니다. 라이프가 남아 있으면 화면 아래에서 **1초 동안** 올라오며 조작 불가·점멸·무적 상태가 되고, 조작이 돌아온 뒤에도 **1초 동안** 점멸·무적이 유지됩니다. 피격 위치에서는 네 조각으로 흩어지는 사망 효과가 재생됩니다.
- 라이프가 0이 되면 생존 타이머를 멈추고 Game Over 화면을 표시합니다. **다시 하기**는 현재 게임 씬을 다시 열고, **타이틀로 이동**은 `MainMenu`로 돌아갑니다.
- 화면 위에는 라이프 3칸, `TIME MM:SS.d` 형식의 누적 생존 시간, 현재 활성 탄환 수가 표시됩니다. 준비 countdown과 buffering 중에는 게임 시간이 흐르지 않습니다.

## 게임 모드

| 메뉴 선택 | 조작과 정보 | 계획·재생 |
| --- | --- | --- |
| 사람 모드 | 플레이어가 키보드로 조작. Jev 호출 없음 | 즉시 시작 |
| 봇 모드, THE ALMIGHTY 끔 | Jev가 플레이어와 활성 탄환의 viewport 위치·속도·위험 정보, 5초 이내 예정 탄환을 받아 9개 이동 방향 중 선택 | 0.2초 간격의 6개 행동(1.2초)을 한 요청으로 계획합니다. 시작 전 5초 countdown과 최소 5초 action buffer를 준비하며, 재생 중 buffer가 0.5초 미만이면 일시 정지하고 보충합니다. 동시 요청은 하나입니다. |
| 봇 모드, THE ALMIGHTY 켬, 외부 Answer 없음 | Jev가 현재 가상 플레이어 위치와 5초 이내 탄환 정보를 사용 | 게임 시작 전에 **0.2초씩 200번 순차 요청**해 **40초**의 행동을 만듭니다. 별도로 **60초**의 탄환 생성 일정을 미리 생성합니다. 계획 중 `The Almighty` 진행률을 표시하고, 완료 후 countdown을 거쳐 저장된 행동을 재생합니다. 40초 이후 행동은 `Stay`입니다. |
| 봇 모드, THE ALMIGHTY 켬, 유효한 외부 Answer 선택 | 외부 AI가 작성한 200개 행동을 사용. 게임 중 Jev 호출 없음 | Jev 계획 단계 없이 같은 Almighty playback 경로로 시작합니다. 해당 Challenge의 탄환 일정을 로드합니다. 40초 이후는 `Stay`입니다. |

Jev 요청은 `jev-latest` 모델의 Choice 질문을 사용합니다. 목표는 탄환을 피하고 오래 생존하는 것이며, 현재 탄환의 위치·velocity·hit size·최근접 접근 정보와 미래 탄환의 생성 시각·위치·velocity를 전달합니다. Jev API 실패는 Bot의 비정상 종료로 기록되고 랭킹에서 제외됩니다. 실제 회피 성능은 네트워크 상태, 응답, 탄막에 따라 달라집니다.

## 메뉴 및 개발 설정

| UI·설정 | 영향 |
| --- | --- |
| **사람 모드 / 봇 모드** | `Main` 씬을 각각 Human / Bot으로 시작합니다. |
| **THE ALMIGHTY** 체크박스 | 봇 모드에만 적용됩니다. 해제하면 일반 Bot, 선택하면 Almighty입니다. 기본값은 해제입니다. |
| **Export Challenge JSON** | 현재 `Main` 씬 규칙으로 Challenge 파일을 생성한 뒤 메뉴로 돌아옵니다. Jev Key는 필요하지 않습니다. |
| **Answer JSON path / Select Plan** | Answer 파일명 또는 절대 경로를 입력하고 검증합니다. 유효한 Answer를 선택한 뒤 THE ALMIGHTY와 봇 모드를 선택하면 외부 계획을 재생합니다. OS 파일 선택 창은 구현되어 있지 않습니다. |
| **랭킹** | 저장된 정상 종료 기록 중 상위 10개를 표시합니다. |

개발자가 `Main` 씬의 `BulletSpawner` Inspector에서 `startBulletsPerSecond`(2), `maxBulletsPerSecond`(20), `secondsToMaxRate`(60), `randomSeed`(12345)를 조정할 수 있습니다. 이는 메뉴 옵션이 아닙니다. Challenge를 만든 뒤 씬 설정이나 카메라 크기를 바꾸면 해당 Answer의 재생이 거부될 수 있습니다.

## Jev API 설정

Unity Editor에서는 프로젝트 루트에 **`.secrets/jev.local.json`** 파일을 만들고 Jev AI에서 발급받은 키를 `apiKey`에 넣습니다. 코드는 `Application.dataPath`의 상위 폴더를 기준으로 읽으므로, standalone 빌드에서는 실행 파일의 `*_Data` 폴더와 같은 위치에 `.secrets/jev.local.json`이 필요합니다. 저장소의 `jev.example.json`은 형식 예시입니다.

```json
{
  "apiKey": "YOUR_JEV_API_KEY"
}
```

현재 코드는 `https://jev-ai.pro/api/v1/systemone`에 `Authorization: Bearer <apiKey>`로 요청하며 모델 이름은 `jev-latest`입니다. Unity Editor의 **Tools → Jev → Check Local API Key**로 로컬 파일을 읽을 수 있는지 확인할 수 있습니다. 키 값은 Console에 출력하지 않습니다. `.secrets/` 전체가 `.gitignore`에 포함되므로 실제 키 파일을 Git에 추가하지 마세요. Human 모드와 외부 Answer 재생에는 Jev Key가 필요하지 않습니다.

**API Key가 없어도 Human 모드는 플레이할 수 있지만, Jev 기반 일반 Bot과 Almighty planning은 사용할 수 없습니다.** 이 모드를 메뉴에서 선택할 때 Key 파일이 없거나 `apiKey`가 비어 있으면 게임 씬으로 이동하지 않고 메뉴에 설정 안내를 표시합니다. Key가 있어도 인증이 거부되면 Game Over 화면에 인증 오류를 표시하며, 해당 판은 비정상 종료로 저장되어 랭킹에 포함되지 않습니다.

## 랭킹과 로컬 기록

게임 한 판이 끝나면 `Application.persistentDataPath/play_records.json`에 JSON 배열(`records`)의 새 항목을 추가합니다. 이는 실행 환경별 로컬 데이터이며 저장소에 포함되지 않습니다. `startedAtIso8601`은 실제 시작 날짜·시각입니다. Almighty는 계획 완료 후 실제 재생 시작 시각으로 갱신합니다.

| 저장 필드 | 의미 |
| --- | --- |
| `firstLifeLostSeconds`, `secondLifeLostSeconds`, `thirdLifeLostSeconds` | 각 라이프를 잃은 **게임 시작 이후 누적 생존 시간**(초). 개별 라이프의 지속시간이 아닙니다. 잃지 않은 라이프는 `-1`입니다. |
| `survivalSeconds` | 종료 시 누적 생존 시간. API 오류도 오류 이전 시간까지 저장합니다. |
| `maxActiveBullets` | 해당 판에서 동시에 활성화되었던 최대 탄환 수. 풀의 비활성 탄환은 제외합니다. |
| `startedAtIso8601`, `mode`, `almighty`, `planName` | 실제 기록 시점, `Human`/`Bot`, Almighty 여부, 외부 Answer를 사용했다면 그 계획 이름입니다. 랭킹 화면의 모드 칸은 `planName`이 있으면 이를 표시합니다. |
| `completedNormally`, `endReason` | 세 번째 피격으로 종료되면 `true`/`GameOver`, Jev API 오류면 `false`/`JevApiError`입니다. |

랭킹은 **정상 종료 기록만** 세 번째 라이프 상실 시점이 긴 순서로 정렬해 상위 10개를 표시합니다. 화면 열은 순위, 첫·둘째·셋째 라이프 상실 시점, 기록 시점, 최대 활성 탄환 수, 모드(또는 외부 계획 이름)입니다. 오래된 JSON에 종료 필드가 없으면 정상 종료 기록으로 읽습니다. 다시 하기는 씬을 새로 열어 타이머와 최대 탄환 수를 초기화합니다.

## Almighty Challenge / Answer JSON

메뉴의 **Export Challenge JSON**을 누르면 `Application.persistentDataPath/almighty_challenges/challenge_<challengeId 앞 16자리>.json`에 파일을 씁니다. `format`은 `6-sol-danmaku-almighty`, `version`은 `1`입니다. `challengeId`는 id를 비운 직렬화 내용의 SHA-256으로, Answer가 어떤 Challenge에 대한 것인지 확인하는 데 사용됩니다.

Challenge에는 `actionInterval: 0.2`, `planningSteps: 200`, `planningDuration: 40`, `scheduleDuration: 60`, 시작 플레이어 viewport 좌표, 이동 속도, 플레이어 collider의 viewport 반경, 스프라이트를 고려한 `playfieldBounds`, `viewportWorldSize`, 허용 행동 9종, 이동 규칙, `maxActiveBullets: 50`, 탄환 반경과 생성·소멸 규칙, 전체 60초 생성 일정이 들어갑니다. `bulletCount`는 생성 *후보* 수이며 동시 활성 수가 아닙니다. 50개 제한 때문에 실제 생성 시 건너뛴 후보가 있을 수 있습니다.

`bulletSchedule`은 각 탄환마다 다음 8개 숫자를 이어 붙인 **평면 배열**입니다. 행 번호 + 1이 탄환 ID입니다. 매 프레임 위치를 중복 저장하지 않습니다.

```text
bulletColumns = [spawnTime, x, y, vx, vy, speed, dirX, dirY]
bulletSchedule = [첫 탄환의 8개 값, 둘째 탄환의 8개 값, ...]
```

`x,y`는 생성 시점의 viewport 위치, `vx,vy`는 초당 viewport 이동량입니다. `speed`는 초당 월드 이동량, `dirX,dirY`는 월드 방향입니다. 생성된 탄환의 시각 `t`에서의 viewport 위치는 `(x + vx × (t - spawnTime), y + vy × (t - spawnTime))`로 계산할 수 있습니다. 플레이어 위치도 이전 action을 0.2초씩 적용하고 대각선을 정규화한 뒤 `playfieldBounds`로 제한해 갱신합니다. 탄환은 화면에 들어온 후 스프라이트 범위까지 화면 밖으로 벗어나거나 플레이어와 충돌하면 풀로 돌아갑니다.

외부 AI의 Answer는 아래 구조입니다. 예시는 형식 설명용이며, **실제 파일에는 `actions`가 정확히 200개** 있어야 합니다. `challengeId`에는 export한 파일의 값을 그대로 사용합니다.

```json
{
  "version": 1,
  "challengeId": "EXPORTED_CHALLENGE_ID",
  "name": "External Plan",
  "actions": ["Left", "DownLeft", "Stay"]
}
```

허용 action은 `Stay`, `Up`, `Down`, `Left`, `Right`, `UpLeft`, `UpRight`, `DownLeft`, `DownRight`입니다. 각 `actions[N]`은 게임 시간 `N × 0.2`초부터 다음 0.2초에 적용됩니다. Answer 파일을 `Application.persistentDataPath/almighty_answers/`에 놓고 메뉴 입력 칸에 **파일명**을 적거나, 파일의 **절대 경로**를 입력해 **Select Plan**을 누릅니다. Unity는 version, 200개 길이, action 값, challengeId 및 해당 Challenge 파일의 내용 해시를 확인합니다. 해당 Challenge 파일도 위 Challenge 폴더에 있어야 합니다. 외부 Answer를 선택한 플레이는 Jev API planning 없이 재생합니다.

Challenge 파일은 60초 탄환 일정을 보존하지만 외부 action은 40초까지만 정의합니다. 이 형식은 플레이어의 시작 상태와 탄환 궤적·생성 제한·이동 규칙을 제공합니다. 라이프와 부활의 세부 규칙은 별도 JSON 필드가 아니라 위 [게임 규칙](#게임-규칙)과 현재 Unity 구현을 기준으로 해야 합니다. 외부 시뮬레이터와 Unity의 피격·프레임 처리까지 완전히 같은 결과가 나오는지는 별도 검증이 필요합니다.

## AI 실험 메모

- **GPT-6 Sol / Codex:** Unity 기능을 AI에게 구현시키고 수정·디버깅을 반복하며 개발 과정과 사용량·할당량을 관찰하는 개인 실험입니다. 통제된 프롬프트나 비교 조건을 갖춘 모델 벤치마크는 아닙니다.
- **Jev:** 탄환의 현재 위치·속도·접근 정보와 5초 미래 생성 정보를 제공하고 이동 방향을 선택하게 합니다. 일반 Bot은 재생 중 미래 계획을 계속 보충하고, Almighty는 게임 전에 200개 행동을 순차적으로 결정합니다. 언어 QA가 아닌 연속적인 2D 공간 회피 판단을 관찰하려는 구성입니다.
- 이 저장소에는 재현 가능한 성능 통계나 모델 간 우열을 결론 낼 자료를 함께 제공하지 않습니다. 기록은 실행한 기기의 로컬 JSON에만 쌓입니다.

## 저장소와 보안

`Assets`, `Packages`, `ProjectSettings`가 Unity 프로젝트의 주요 소스입니다. `.gitignore`는 `.secrets/`, `Library/`, `Temp/`, `Obj/`, `Build/`, `Builds/`, `Logs/`, `UserSettings/` 및 `*.log`를 제외합니다. API Key, 로컬 플레이 기록, Challenge/Answer 파일, 생성 로그를 커밋하지 마세요. 실제 키 대신 `jev.example.json`의 placeholder를 참고하세요.
