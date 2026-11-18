#!/usr/bin/env bash
# multibot-update — забирает последний релиз MultiBot из GitHub и выкатывает его.
#
# Запускается таймером multibot-update.timer (раз в 5 минут) или вручную:
#   systemctl start multibot-update      # проверить и выкатить сейчас
#   journalctl -u multibot-update -n 50  # что происходило
#
# Порядок: скачать → сверить sha256 → проверить содержимое → распаковать
# в /opt/multibot/releases/<тег> → остановить бота → бэкап БД → переключить
# ссылки app и webapp → запустить → проверить /health. Если бот не поднялся —
# вернуть прежний релиз и БД из бэкапа и больше не пытаться ставить этот тег.
#
# Настройки — /etc/multibot/updater.env (права 600):
#   REPO=owner/repo                 обязательно
#   GITHUB_TOKEN=github_pat_...     для приватного репозитория (Contents: Read-only)
#   WEBHOOK_IP=<IP>                 если задан — после запуска webhook
#                                   перерегистрируется с этим ip_address
#   NOTIFY_CHAT_ID=123456789        если задан — бот пишет сюда об итогах

set -euo pipefail
umask 022
export LC_ALL=C

# Пути можно переопределить через окружение (используется только в тестах).
CONF=${MBU_CONF:-/etc/multibot/updater.env}
BOT_ENV=${MBU_BOT_ENV:-/etc/multibot/multibot.env}
BASE=${MBU_BASE:-/opt/multibot}
REL_DIR=${BASE}/releases
STATE_DIR=${MBU_STATE_DIR:-/var/lib/multibot-updater}
BACKUP_DIR=${MBU_BACKUP_DIR:-/var/backups/multibot}
DB=${MBU_DB:-/var/lib/multibot/bot.db}
LOCK=${MBU_LOCK:-/run/multibot-update.lock}
SERVICE=multibot
HEALTH_URL=http://127.0.0.1:5080/health
ASSET=multibot.tar.gz
KEEP_RELEASES=3
KEEP_BACKUPS=10

REPO=; GITHUB_TOKEN=; WEBHOOK_IP=; NOTIFY_CHAT_ID=
# shellcheck disable=SC1090
. "${CONF}"
[[ ${REPO} =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || { echo "ERROR: REPO в ${CONF} не задан или неверен" >&2; exit 1; }

BOT_TOKEN=$(sed -n 's/^Bot__Token=//p' "${BOT_ENV}")
BOT_SECRET=$(sed -n 's/^Bot__WebhookSecret=//p' "${BOT_ENV}")
PUBLIC_URL=$(sed -n 's/^Bot__PublicUrl=//p' "${BOT_ENV}")

# Секреты не должны попадать в аргументы процессов (их видят все пользователи
# через /proc), поэтому заголовки и параметры передаются через файлы и stdin.
WORK=$(mktemp -d "${MBU_TMP:-/var/tmp}/multibot-update.XXXXXX")
trap 'rm -rf "${WORK}"' EXIT
AUTH="${WORK}/github.curl"
: > "${AUTH}"
if [ -n "${GITHUB_TOKEN}" ]; then
  printf 'header = "Authorization: Bearer %s"\n' "${GITHUB_TOKEN}" > "${AUTH}"
fi

log() { printf '%s\n' "$*"; }

# tg МЕТОД [строки конфигурации curl...] — запрос к Bot API
tg() {
  local method=$1; shift
  {
    printf 'url = "https://api.telegram.org/bot%s/%s"\n' "${BOT_TOKEN}" "${method}"
    local line
    for line in "$@"; do printf '%s\n' "${line}"; done
  } | curl -fsS --max-time 20 -K -
}

notify() {
  [ -n "${NOTIFY_CHAT_ID}" ] && [ -n "${BOT_TOKEN}" ] || return 0
  local text=${1//\"/\'}
  tg sendMessage "data = \"chat_id=${NOTIFY_CHAT_ID}\"" \
    "data-urlencode = \"text=MultiBot: ${text}\"" >/dev/null 2>&1 || true
}

die() { log "ERROR: $*" >&2; notify "ошибка обновления: $*"; exit 1; }
# Временная проблема (сеть, GitHub): без уведомления, следующий запуск повторит.
retry_later() { log "WARN: $*, повторю при следующем запуске" >&2; exit 1; }
# Проблема с конкретным релизом: запомнить тег, чтобы не повторять его при каждом запуске.
bad_release() { echo "${TAG}" > "${STATE_DIR}/failed"; die "$@"; }

# gh URL ACCEPT [аргументы curl...] — запрос к GitHub
gh() {
  local url=$1 accept=$2; shift 2
  curl -fsSL --retry 3 --retry-delay 5 --max-time 600 \
    -K "${AUTH}" -H "Accept: ${accept}" -H "X-GitHub-Api-Version: 2022-11-28" \
    "$@" "${url}"
}

# Переключить ссылки app и webapp на релиз (подмена ссылки атомарная).
point_to() {
  ln -sfn "releases/$1/app" "${BASE}/.app.new"
  mv -Tf "${BASE}/.app.new" "${BASE}/app"
  ln -sfn "releases/$1/webapp" "${BASE}/.webapp.new"
  mv -Tf "${BASE}/.webapp.new" "${BASE}/webapp"
}

healthy() {
  local i
  for i in $(seq 1 30); do
    if systemctl is-active --quiet "${SERVICE}" &&
       curl -fs -o /dev/null --max-time 3 "${HEALTH_URL}"; then
      return 0
    fi
    sleep 2
  done
  return 1
}

# Бот сам регистрирует webhook при старте, но без ip_address. Пока в проекте
# нет такой настройки, ставим её сами — и проверяем несколько раз, потому что
# регистрация из бота может прийти позже нашей.
fix_webhook() {
  [ -n "${WEBHOOK_IP}" ] && [ -n "${BOT_TOKEN}" ] || return 0
  local want="${PUBLIC_URL}/bot/webhook" info url ip allowed i
  for i in 1 2 3 4 5 6; do
    sleep 5
    info=$(tg getWebhookInfo) || continue
    url=$(jq -r '.result.url // empty' <<<"${info}")
    ip=$(jq -r '.result.ip_address // empty' <<<"${info}")
    if [ "${url}" = "${want}" ] && [ "${ip}" = "${WEBHOOK_IP}" ]; then
      log "webhook: ${url} → ${ip}"
      return 0
    fi
    allowed=$(jq -c '.result.allowed_updates // ["message","callback_query"]' <<<"${info}")
    tg setWebhook "data-urlencode = \"url=${want}\"" "data = \"ip_address=${WEBHOOK_IP}\"" \
      "data = \"secret_token=${BOT_SECRET}\"" \
      "data-urlencode = \"allowed_updates=${allowed//\"/\\\"}\"" >/dev/null || true
  done
  log "WARN: webhook не удалось закрепить на ${WEBHOOK_IP}"
  notify "webhook не закреплён на ${WEBHOOK_IP}, проверьте getWebhookInfo"
}

backup_db() { # $1 каталог бэкапа; бот должен быть остановлен
  mkdir -p "$1"
  local f
  for f in "${DB}" "${DB}-wal" "${DB}-shm"; do
    [ -e "${f}" ] && cp -a "${f}" "$1/"
  done
  return 0
}

restore_db() { # $1 каталог бэкапа; бот должен быть остановлен
  [ -e "$1/$(basename "${DB}")" ] || return 0
  rm -f "${DB}-wal" "${DB}-shm"
  cp -a "$1/." "$(dirname "${DB}")/"
}

cleanup_old() {
  local d current
  current=$(readlink "${BASE}/app" | cut -d/ -f2)
  # shellcheck disable=SC2012
  { ls -1dt "${REL_DIR}"/*/ 2>/dev/null || true; } | tail -n +$((KEEP_RELEASES + 1)) | while read -r d; do
    [ "$(basename "${d}")" = "${current}" ] || rm -rf "${d}"
  done
  # shellcheck disable=SC2012
  { ls -1dt "${BACKUP_DIR}"/*/ 2>/dev/null || true; } | tail -n +$((KEEP_BACKUPS + 1)) | while read -r d; do
    rm -rf "${d}"
  done
}

# ---------------------------------------------------------------- начало

exec 9>"${LOCK}"
flock -n 9 || { log "уже выполняется, пропуск"; exit 0; }
mkdir -p "${STATE_DIR}" "${REL_DIR}"
install -d -m 0700 "${BACKUP_DIR}"

# Первый запуск: app и webapp — обычные каталоги после ручного деплоя.
# Переносим их в releases/initial и заменяем ссылками.
if [ -d "${BASE}/app" ] && [ ! -L "${BASE}/app" ]; then
  log "перевожу ручную установку в releases/initial"
  mkdir -p "${REL_DIR}/initial"
  mv "${BASE}/app" "${REL_DIR}/initial/app"
  mv "${BASE}/webapp" "${REL_DIR}/initial/webapp"
  point_to initial
  echo initial > "${STATE_DIR}/current"
fi

CURRENT=$(cat "${STATE_DIR}/current" 2>/dev/null || true)
FAILED=$(cat "${STATE_DIR}/failed" 2>/dev/null || true)

RELEASE=$(gh "https://api.github.com/repos/${REPO}/releases/latest" application/vnd.github+json) \
  || retry_later "GitHub API не ответил (репозиторий ${REPO}, токен?)"
TAG=$(jq -r '.tag_name // empty' <<<"${RELEASE}")
[[ ${TAG} =~ ^[A-Za-z0-9._-]+$ ]] || retry_later "нет релизов или странный тег: '${TAG}'"

if [ "${TAG}" = "${CURRENT}" ]; then exit 0; fi
if [ "${TAG}" = "${FAILED}" ]; then
  log "релиз ${TAG} уже не прошёл проверку, жду следующий"
  exit 0
fi

log "новый релиз: ${TAG} (сейчас: ${CURRENT:-нет})"
ASSET_URL=$(jq -r --arg n "${ASSET}" '.assets[] | select(.name == $n) | .url' <<<"${RELEASE}")
SUM_URL=$(jq -r --arg n "${ASSET}.sha256" '.assets[] | select(.name == $n) | .url' <<<"${RELEASE}")
[ -n "${ASSET_URL}" ] && [ -n "${SUM_URL}" ] || bad_release "в релизе ${TAG} нет ${ASSET} или ${ASSET}.sha256"

gh "${ASSET_URL}" application/octet-stream -o "${WORK}/${ASSET}" || retry_later "не скачался ${ASSET}"
gh "${SUM_URL}" application/octet-stream -o "${WORK}/${ASSET}.sha256" || retry_later "не скачалась контрольная сумма"
(cd "${WORK}" && sha256sum -c --quiet "${ASSET}.sha256") || bad_release "контрольная сумма ${TAG} не сходится"

# В архиве допустимы только app/, webapp/ и COMMIT.
LIST=$(tar -tzf "${WORK}/${ASSET}") || bad_release "архив ${TAG} повреждён"
BAD=$(grep -vE '^(\./)?((app|webapp)(/.*)?|COMMIT)$' <<<"${LIST}" || true)
[ -z "${BAD}" ] || bad_release "в архиве ${TAG} лишние пути: $(head -3 <<<"${BAD}" | tr '\n' ' ')"
if grep -qE '(^|/)\.\.(/|$)' <<<"${LIST}"; then bad_release "в архиве ${TAG} есть пути с .."; fi

NEW="${REL_DIR}/${TAG}"
rm -rf "${NEW}.tmp"
mkdir -p "${NEW}.tmp"
tar -xzf "${WORK}/${ASSET}" -C "${NEW}.tmp" --no-same-owner
[ -x "${NEW}.tmp/app/Bot.Host" ] || bad_release "в ${TAG} нет исполняемого app/Bot.Host"
[ -f "${NEW}.tmp/webapp/index.html" ] || bad_release "в ${TAG} нет webapp/index.html"
chown -R root:root "${NEW}.tmp"
chmod -R go-w "${NEW}.tmp"
rm -rf "${NEW}"
mv "${NEW}.tmp" "${NEW}"

BK="${BACKUP_DIR}/$(date +%Y%m%d-%H%M%S)-before-${TAG}"
log "останавливаю ${SERVICE}, бэкап БД в ${BK}"
systemctl stop "${SERVICE}"
backup_db "${BK}"
point_to "${TAG}"
systemctl start "${SERVICE}" || true

if healthy && sleep 5 && healthy; then
  echo "${TAG}" > "${STATE_DIR}/current"
  rm -f "${STATE_DIR}/failed"
  log "релиз ${TAG} работает"
  fix_webhook
  cleanup_old
  notify "выкачен ${TAG} ($(cut -c1-7 "${NEW}/COMMIT" 2>/dev/null || echo '?'))"
  exit 0
fi

log "релиз ${TAG} не поднялся, последние строки журнала:"
journalctl -u "${SERVICE}" -n 40 --no-pager || true
echo "${TAG}" > "${STATE_DIR}/failed"
systemctl stop "${SERVICE}" || true
if [ -n "${CURRENT}" ] && [ -d "${REL_DIR}/${CURRENT}" ]; then
  restore_db "${BK}"
  point_to "${CURRENT}"
  systemctl start "${SERVICE}" || true
  if healthy; then
    fix_webhook
    die "релиз ${TAG} не поднялся, возвращён ${CURRENT}"
  fi
  die "релиз ${TAG} не поднялся, и откат на ${CURRENT} тоже не помог — нужен ручной разбор"
fi
die "релиз ${TAG} не поднялся, откатываться не на что"
