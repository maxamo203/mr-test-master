#!/bin/zsh
# Compila el proyecto Xcode que exporta Unity con Xcode 26 (SDK de iOS 26) y lo instala
# en el iPhone conectado.
#
# Por qué Xcode 26 y no el Xcode por defecto: con el SDK de iOS 27 fallan features de ARKit
# con la cámara. La instalación se hace con el devicectl del Xcode por defecto, porque
# Xcode 26 no tiene soporte de dispositivo para iOS 27.
#
# Uso:
#   ./compilar-ios.sh                    # usa ~/Desktop/build9
#   ./compilar-ios.sh <carpeta-export>   # otra carpeta exportada por Unity
#   DEVICE=<UDID> ./compilar-ios.sh      # si hay más de un iPhone conectado

set -euo pipefail

EXPORT_DIR="${1:-$HOME/Desktop/build9}"
XCODE_26="/Applications/Xcode_26.app/Contents/Developer"
XCODE_DEFAULT="/Applications/Xcode.app/Contents/Developer"
DERIVED="$EXPORT_DIR/build/DerivedData"
LOG="$EXPORT_DIR/build/xcodebuild.log"

[[ -d "$XCODE_26" ]] || { echo "No encuentro Xcode 26 en /Applications/Xcode_26.app"; exit 1; }
[[ -d "$EXPORT_DIR/Unity-iPhone.xcodeproj" ]] || { echo "No hay Unity-iPhone.xcodeproj en $EXPORT_DIR"; exit 1; }

# iPhone físico conectado (o el UDID que se pase por DEVICE)
if [[ -z "${DEVICE:-}" ]]; then
  JSON=$(mktemp)
  DEVELOPER_DIR="$XCODE_DEFAULT" xcrun devicectl list devices --json-output "$JSON" >/dev/null 2>&1 || true
  DEVICE=$(/usr/bin/python3 - "$JSON" <<'PY'
import json, sys
try:
    devs = json.load(open(sys.argv[1]))["result"]["devices"]
except Exception:
    devs = []
for d in devs:
    hw = d.get("hardwareProperties", {})
    if hw.get("reality") == "physical" and hw.get("platform") == "iOS" and hw.get("deviceType") == "iPhone":
        print(hw.get("udid", "")); break
PY
)
  rm -f "$JSON"
fi
[[ -n "$DEVICE" ]] || { echo "No hay ningún iPhone conectado. Conectalo por cable, desbloquealo y probá de nuevo."; exit 1; }

echo "==> Compilando con Xcode 26 ($EXPORT_DIR)… log: $LOG"
mkdir -p "$(dirname "$LOG")"
if ! DEVELOPER_DIR="$XCODE_26" xcodebuild \
    -project "$EXPORT_DIR/Unity-iPhone.xcodeproj" \
    -scheme Unity-iPhone \
    -configuration Release \
    -sdk iphoneos \
    -destination 'generic/platform=iOS' \
    -derivedDataPath "$DERIVED" \
    -allowProvisioningUpdates \
    build > "$LOG" 2>&1; then
  echo "La compilación falló. Errores:"
  grep -E "error:" "$LOG" | head -20
  exit 1
fi

APP=$(find "$DERIVED/Build/Products/Release-iphoneos" -maxdepth 1 -name "*.app" | head -1)
SDK=$(/usr/libexec/PlistBuddy -c "Print :DTSDKName" "$APP/Info.plist")
echo "==> Compilado: $(basename "$APP") con SDK $SDK"

echo "==> Instalando en el iPhone $DEVICE…"
DEVELOPER_DIR="$XCODE_DEFAULT" xcrun devicectl device install app --device "$DEVICE" "$APP"
echo "==> Listo. Abrí la app en el iPhone."
