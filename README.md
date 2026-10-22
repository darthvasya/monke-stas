# Stasik

Шуточная треш-игра про стримера и его комьюнити. Выходит сезонами.

## Как открыть
1. Unity Hub → **Add → Add project from disk** → выбрать папку `game/`.
2. Версия Unity: **6000.6.6f1** (URP).
3. При первом открытии Unity скачает пакеты (в т.ч. Ink) — нужен интернет.

## Где что
- `docs/` — дизайн: концепт, системы, сезоны, персонажи, ассеты.
- `game/` — Unity-проект. Всё наше лежит в `game/Assets/_Project/`.
- `game/Assets/_Project/Story/` — сценарии на Ink.
- `CLAUDE.md` — правила проекта (для Claude и людей).

## Git
- Нужен **Git LFS** для моделей, текстур и звуков: `git lfs install` один раз.
- Папки `Library/`, `Temp/`, `Logs/`, `UserSettings/` не коммитим.
