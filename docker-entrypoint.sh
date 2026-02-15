#!/bin/bash
set -e

if [ "$#" -gt 0 ]; then
	exec "$@"
fi

echo "Obtaining Kerberos ticket..."

KRB5_KEYTAB_PATH="${KRB5_KEYTAB_PATH:-/etc/security/keytabs/service.keytab}"
KRB5_PRINCIPAL="${KRB5_PRINCIPAL:-HTTP/CHANGE_THIS_TO_YOUR_KRB5_PRINCIPAL}"

export KRB5CCNAME=FILE:/tmp/krb5cc_$(id -u)

kdestroy || true

if [ ! -f "$KRB5_KEYTAB_PATH" ]; then
	echo "ERROR: keytab not found at $KRB5_KEYTAB_PATH"
	exit 1
fi

kinit -kt "$KRB5_KEYTAB_PATH" "$KRB5_PRINCIPAL"

echo "Ticket acquired:"
klist

echo "Starting application..."
exec dotnet ad-dotnet-10-example.dll
