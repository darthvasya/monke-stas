# Stasik — правила проекта

Шуточная треш-игра про стримера и его комьюнити. Играешь за стримера: квартира в Нидерландах, борщ,
тренажерка, ссоры с женой, CS, стримы, друзья. Игра выходит **сезонами**: каждый сезон про одного
персонажа из комьюнити. Цель — поржать, не реализм.

## Текущий фокус
**Сезон 0, День 1** (см. `docs/seasons/season-00-pilot.md`). Не делать то, что не нужно для Дня 1,
пока пользователь не скажет. Идеи на будущее записывать в документацию, а не в код.

## Структура
```
CLAUDE.md            правила (этот файл)
README.md            как открыть и запустить
docs/                дизайн: концепт, системы, сезоны, персонажи, ассеты
  seasons/           план каждого сезона
  characters/        карточки персонажей (стример, жена, комьюнити)
game/                Unity-проект (открывать в Unity Hub именно эту папку)
  Assets/_Project/   ВСЁ наше: код, сценарии, арт, сцены
  Assets/ThirdParty/ купленные/скачанные ассеты (не править, обновлять целиком)
tools/               скрипты-помощники вне Unity
```

Внутри `game/Assets/_Project/`:
```
Scripts/<Система>/   Core, Hub, Dialogue, Battle, Rhythm, Stream, Minigames/<Имя>
Scripts/Player/      управление игроком: ввод, движение, камера от 3-го лица, анимации
Scripts/Characters/  общее для персонажей (мех FurShells, рука CharacterHand, кепка HeadCap)
Scripts/Interaction/ предметы и места для них (Holdable, HoldSpot) — фундамент, не система; системы могут на него ссылаться
Scripts/Editor/      редакторские инструменты (меню Stasik/...), asmdef только для Editor
Story/Season00/      Ink-сценарии (.ink), по файлу на день
Scenes/              сцены (Boot, Apartment, ...)
Prefabs/  ScriptableObjects/  Settings/
Art/      Models, Materials, Textures, Animations, UI, Faces
Audio/    Music, SFX, Voice
```

## Движок и стек
- Unity **6000.6.6f1**, URP. Не менять версию без спроса.
- Сценарии и диалоги — **Ink** (`.ink` в `Story/`), плагин ink-unity-integration.
- Анимации — Mixamo. Модели — low-poly (Synty / Kenney / Quaternius).
- Ввод — новый Input System.

## Пять систем (переиспользуются во всех сезонах)
1. **Hub** — день, энергия, выбор дел.
2. **Dialogue** — Ink.
3. **Battle** — батл подколов (жена, реп-батлы, комьюнити). Главная механика.
4. **Rhythm** — балалайка, тренажерка, реп.
5. **Stream** — экран стрима: чат, донаты, зрители, итог дня.

Новый контент — это данные (Ink, ScriptableObject), а не новый код. Если для нового сезона
приходится менять систему — сначала обсудить.

## Код
- C#, имена и комментарии в коде на английском. Общение с пользователем — на русском.
- Namespace: `Stasik.<Система>` (например `Stasik.Battle`).
- Одна система — одна папка и свой `.asmdef`. Системы не ссылаются друг на друга напрямую,
  общаются через `Stasik.Core` (события / сервисы).
- Тексты, подколы, фразы чата — не хардкодить в C#, держать в Ink или ScriptableObject.
- Классы `PascalCase`, приватные поля `_camelCase`, сериализуемые — `[SerializeField] private`.

## Unity и git
- Никогда не трогать `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `obj/` — они в `.gitignore`.
- Каждый файл в `Assets/` имеет `.meta`: создавать/перемещать/удалять файлы **вместе с .meta**.
  Лучше перемещать ассеты через Unity, а не через файловую систему.
- Сцены и префабы — Force Text (YAML), чтобы были видны диффы.
- Большие бинарные файлы (fbx, png, wav...) идут через Git LFS (см. `.gitattributes`).
- Коммитить и пушить только когда пользователь попросит.

## Unity MCP
- Пакет `com.coplaydev.unity-mcp` (MCP for Unity). Сервер `UnityMCP` прописан в Claude Code для этого
  проекта: HTTP `http://127.0.0.1:8080/mcp`.
- Сервер запускается из Unity: **Window → MCP for Unity → Start Server** (после перезапуска Unity — снова).
- Через MCP: читать Console, создавать/менять объекты, запускать меню и Play mode — вместо кликов по экрану.

## Модели в SolidCraft
- Геометрию (квартира и т.п.) строим скриптами в `tools/` через порт управления SolidCraft
  (`tools/solidcraft.py`). SolidCraft должен быть запущен с `--control 7878`.
- Источник правды — скрипт (`tools/build_apartment.py`), а не ручные правки в модели.
  Правка → пересборка `python tools/build_apartment.py --build`.
- Экспорт для Unity — OBJ (SolidCraft не умеет FBX/GLB); скрипт сам переводит в метры и Y-up.
- План квартиры и размеры: `docs/apartment-plan.md`.
- После пересборки модели — в Unity меню **Stasik → Build Apartment Scene**
  (`Scripts/Editor/ApartmentSceneBuilder.cs`): материалы, коллайдеры, свет, сцена `Scenes/Apartment`.
  Сцену `Apartment` руками не править — её пересоздаёт этот скрипт (своё — в отдельные сцены/префабы).
- Мебель — таблица в `Scripts/Editor/ApartmentFurniture.cs` (Kenney, 1 ед. = 0.2 м, ставится по габаритам,
  перед моделей смотрит в −Z). Двигать мебель = править таблицу и пересобрать сцену.

## Персонаж (стример-обезьянка)
- Пайплайн и файлы: `docs/characters/streamer.md` (раздел «1в1 с концептом»).
- Генерация (fal.ai MCP): концепт → виды → Hunyuan 3D по 4 ракурсам. Концепт — источник правды.
- `Scripts/Editor/ConceptProjectionBaker.cs`: FitToView (Front, Left) → Bake (4 вида: спереди, слева, справа, сзади,
  вырезки в `Characters/Streamer/Concept/*_Cutout.png`), всегда на **свежем** экземпляре
  модели (стоит вертикально, лицом в −Z). Шейдеры: `Stasik/CharacterPhoto`, `Stasik/FurShell` (+ `FurShells`).
- Сравнивать с концептом только «студийной» камерой: спереди, издалека, узкий угол, светлый фон.
- Сборка персонажа целиком: меню **Stasik → Characters → Build Streamer Prefab**
  (`StreamerCharacterBuilder.cs`) → `Prefabs/Characters/Streamer.prefab` (статичная модель + запечённая текстура).
- Скелет и анимации: fal `meshy/rigging/multi-animation` по GLB-экспорту запечённой модели → `Rigged/Streamer_Rigged.fbx`;
  FBX анимаций кладутся в `Art/Animations/Streamer/_Import/` (по 30 МБ — не коммитить), затем меню
  **Stasik → Characters → Build Player Prefab** (`PlayerCharacterBuilder.cs`): извлекает клипы в `.anim`, удаляет FBX,
  собирает `Streamer.controller` и `Prefabs/Characters/Player.prefab`. Сборщик сцены ставит Player + PlayerCamera.
- Кепка: `CapBuilder.cs` (меш под голову) + `python tools/make_cap_texture.py` (текстура по `cap_dims.json`),
  кепка — предмет `Prefabs/Items/Cap.prefab` (`Holdable` + `HeadCap`: вперёд / задом наперёд, два меша под голову,
  мех под ней прячется). `PlayerCharacterBuilder` подгоняет её под голову игрока при каждой сборке
  (`CapBuilder.BuildCapPrefab`). День начинается с кепкой на вешалке (крючок `CapHook`).
  Тест: **Stasik → Characters → Cap Test Scene** (`Scenes/Sandbox/CapTest.unity`).
- Предметы: `Holdable` (якоря: хват, подвес, низ, поза переноски) и `HoldSpot` (крючок / поверхность). Новый предмет —
  префаб с `Holdable`, новое место — строка в таблице `Spots` в `ApartmentFurniture.cs`, без нового кода.
  Рука — `CharacterHand` (Animation Rigging: Two Bone IK на правую руку, риг строит `PlayerCharacterBuilder`): любое
  действие = перенос предмета из места A в место B. Поворот кисти задаётся целью IK как поворот кости
  (у этого скелета пальцы вдоль +Y кости, ладонь в −Z). `Animator.body*` читать только внутри IK — у нас не используется.
- Порядок сборки: Build Player Prefab (пишет и Cap.prefab) → Build Apartment Scene.
  Подробности: `docs/characters/streamer.md`, раздел «Кепка».
- Майка: боковые и задний виды запекаются **только на голову**; полоски на боках/спине/рукавах берутся с фронтального
  концепта на той же высоте («профиль полосок»), поэтому идут по кругу без стыков.
- Плечи в клипах держатся на месте (`HoldShoulders` в `PlayerCharacterBuilder`): клипы риггера задирали плечи в Idle.
  Руки и корпус при этом не сдвигаются (пересчёт позы на уровне костей).
- Веса скиннинга туловища правит постпроцессор импорта `StreamerSkinFix.cs` (авториг привязал спину к рукам и голове →
  полоски гнулись «галочкой»). Правка весов = поменять скрипт и поднять `GetVersion()`, FBX переимпортируется сам.

## Управление и камера
- Камера как в Ведьмаке: `ThirdPersonCamera` (за плечом, мышь/правый стик, колесо — дистанция, упирается в стены).
- WASD/стик — ходьба, Shift — бег, колесо — дистанция камеры, Esc — курсор.
- E / кнопка юга — взаимодействие (`PlayerInteract`): взять предмет, повесить на крючок, поставить на стол или положить
  перед собой. Если не дотягивается — сам подходит и поворачивается; стик отменяет подход. Пока рука работает с местом
  в мире, ходьба заблокирована (лок с владельцем: `PlayerMovement.AddLock/RemoveLock`).
- C / крестовина вверх — кепка: в руке → надеть козырьком вперёд → перевернуть → снять в руку.
- **Скорость ходьбы/бега не задаётся руками**: `PlayerCharacterBuilder` меряет «родную» скорость клипа по стоящей
  ступне (`GaitAnalyzer`) и ставит её и в `PlayerMovement`, и в пороги блендтри — иначе ноги скользят («парит»).
  Ускорить шаг — `WalkPlayback`/`RunPlayback` в билдере, скорость пересчитается сама.
- Сцену и игрока нельзя пересобирать в Play mode (билдеры откажутся).
- Анимации: блендтри Locomotion (Idle/Walk/Run по `Speed` в м/с) + состояния Interact, SitIdle, Talk, TalkAngry, Sleep,
  Dance, Cheer — запуск `Animator.CrossFade("Имя", 0.2f)`.
- Только новый Input System: `UnityEngine.Input` бросает исключение.

## Ассеты и лицензии
- Можно: CC0, CC-BY (записать автора в `docs/assets-credits.md`), купленные на Asset Store.
- Нельзя: **NC (non-commercial)**, ассеты «только для Unreal» с Fab, рипы из чужих игр.
- Каждый сторонний ассет — запись в `docs/assets-credits.md`: что, откуда, лицензия.

## Тон и реальные люди
- Персонажи — реальные друзья. Юмор — дружеские подколы, которые сами герои одобрят.
- Ссора с женой — **словесная** (батл подколов), без физического насилия.
- Без настоящих личных данных (адреса, места работы и т.п.).
- Стиль: нарочито кривой low-poly, ragdoll, пафос не к месту.
