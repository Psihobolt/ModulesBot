---
name: telegram-mini-app
description: Знания о Telegram Mini Apps (WebView) — запуск, проверка initData на сервере, deep links startapp, тема, MainButton/BackButton, локальная разработка. Используй при любой задаче, где есть экраны Mini App или API, которое они вызывают.
---

# Telegram Mini App — правила проекта

Перед реализацией сверяйся с официальной документацией: https://core.telegram.org/bots/webapps
(API Mini Apps развивается; если что-то здесь расходится с документацией — права документация, отметь расхождение в отчёте).

## Как открывается Mini App
- Кнопка меню бота или inline-кнопка `web_app` → открывает URL приложения.
- Прямая ссылка `https://t.me/<bot>/<app>?startapp=<param>` (приложение создаётся в BotFather через `/newapp`).
  `<param>` приходит в `initData` как `start_param`. Формат в проекте: `<module>_<payload>`,
  например `movienight_join_AB12CD`. Разрешены только `A-Za-z0-9_-`, до 64 символов.
- Только HTTPS. Для локальной разработки — туннель (cloudflared/ngrok) + отдельный тестовый бот.

## Авторизация (backend) — обязательно
Клиент шлёт сырую строку `Telegram.WebApp.initData` в заголовке `Authorization: tma <initData>`.
Сервер в middleware платформы:
1. Разбирает query-string, достаёт `hash`.
2. `data_check_string` = все остальные поля `key=value`, отсортированные по ключу, через `\n`.
3. `secret_key = HMAC_SHA256(key: "WebAppData", data: bot_token)`.
4. Сравнивает `hex(HMAC_SHA256(key: secret_key, data: data_check_string))` с `hash`
   через `CryptographicOperations.FixedTimeEquals`.
5. Проверяет свежесть `auth_date` (по умолчанию не старше 24 ч, настраивается).
6. Кладёт `user.id` в `HttpContext` как `TelegramUserId`. Модули берут пользователя только отсюда.
Любая ошибка → 401. Тесты: валидная подпись, подменённое поле, просроченный `auth_date`, без заголовка.

## Frontend
- Подключить `https://telegram.org/js/telegram-web-app.js` до бандла; обёртка в `webapp/src/platform/telegram.ts`,
  модули не обращаются к `window.Telegram` напрямую (это же даёт мок для тестов).
- При старте: `WebApp.ready()`, при необходимости `WebApp.expand()`.
- Цвета только через CSS-переменные темы (`var(--tg-theme-bg-color)`, `--tg-theme-text-color`,
  `--tg-theme-button-color` …). Проверять в светлой и тёмной теме.
- Главное действие экрана — `WebApp.MainButton`, навигация назад — `WebApp.BackButton`, а не свои кнопки.
- Тактильный отклик (`HapticFeedback`) — на значимые события (например, результат рандомайзера).
- Mini App может открываться вне Telegram (в браузере) — показать понятную заглушку, а не падать.
- Роутинг: `start_param` → маршрут модуля, разбор в `platform/router`.

## Связь бота и Mini App
- Действие в Mini App, о котором надо сообщить другим участникам, → API → сервер шлёт сообщение ботом.
- Бот не может написать пользователю, который его не запускал (403) — обрабатывать и не падать.
