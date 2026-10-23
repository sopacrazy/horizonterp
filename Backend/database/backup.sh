#!/bin/bash
set -euo pipefail
umask 077
backup_dir=/srv/hbrp/backups
stamp=$(date -u +%Y%m%dT%H%M%SZ)
file="$backup_dir/hbrp-$stamp.dump"
test -d "$backup_dir"
runuser -u postgres -- pg_dump -p 55432 -d hbrp --format=custom --no-owner > "$file.partial"
runuser -u postgres -- pg_restore --list < "$file.partial" > /dev/null
mv -- "$file.partial" "$file"
# Only HBRP-generated archives in this exact dedicated directory are retained for 14 days.
find "$backup_dir" -maxdepth 1 -type f -name 'hbrp-????????T??????Z.dump' -mtime +14 -delete
printf 'HBRP_BACKUP_OK %s\n' "$file"
