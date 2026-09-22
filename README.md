# Exerussus Outline

Мягкий настраиваемый аутлайн для Unity URP (Render Graph). Контур строится в экранном пространстве по полю расстояний (Jump Flood). Поддерживаются разные цвета и стили для разных объектов, внутренний контур, заливка, rim, паттерны, отдельный стиль для перекрытой части, анимация, группы и приоритеты.

| Пакет | Содержимое |
|---|---|
| `com.exerussus.outline` | Рантайм, шейдеры, фича рендерера, установщик фичи |
| `com.exerussus.outline.lab` | Тестовая сцена, пресеты стилей, демо, HUD, бенчмарк (необязателен) |

## Требования

- Unity 6000.6
- URP 17.6

## Установка

Package Manager → **+** → **Install package from git URL**:

```
https://github.com/exerussus/outline.git?path=/Packages/com.exerussus.outline
```

Площадка (ставится после основного пакета):

```
https://github.com/exerussus/outline.git?path=/Packages/com.exerussus.outline.lab
```

Или в `Packages/manifest.json`:

```json
"com.exerussus.outline": "https://github.com/exerussus/outline.git?path=/Packages/com.exerussus.outline",
"com.exerussus.outline.lab": "https://github.com/exerussus/outline.git?path=/Packages/com.exerussus.outline.lab"
```

После установки: **Exerussus → Outline → Подключить к рендерерам URP**.

Репозиторий — Unity-проект с обоими пакетами. Чтобы открыть площадку, откройте проект и выберите **Exerussus → Outline → Lab → Собрать сцену OutlineLab**.

## Документация

[Packages/com.exerussus.outline/README.md](Packages/com.exerussus.outline/README.md)

## Лицензия

MIT
