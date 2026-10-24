#!/usr/bin/env bash
# Runs FitCore.Maui.UITests against the app on a booted Android emulator (CI's "Android app on an emulator" job, or
# your own). Needs: the emulator up (adb sees it), the local Supabase stack running, Appium 3 with the UiAutomator2
# driver on the PATH, and FITCORE_ANDROID_APK pointing at an x86_64 Debug APK with its assemblies embedded:
#
#   dotnet build src/FitCore.Maui -f net10.0-android -p:RuntimeIdentifier=android-x64 -p:EmbedAssembliesIntoApk=true -o artifacts/apk
#   FITCORE_ANDROID_APK=artifacts/apk/app.fitcore-Signed.apk tests/FitCore.Maui.UITests/run-on-emulator.sh
#
# Appium's log, the device log and, if a wait times out, a screenshot go to FITCORE_UITEST_ARTIFACTS.
set -euo pipefail

: "${FITCORE_ANDROID_APK:?Set FITCORE_ANDROID_APK to the APK to test.}"
[[ -f "$FITCORE_ANDROID_APK" ]] || { echo "No APK at $FITCORE_ANDROID_APK" >&2; exit 1; }

# Absolute, since dotnet test runs the tests from their bin directory, not from here.
FITCORE_ANDROID_APK="$(realpath "$FITCORE_ANDROID_APK")"
export FITCORE_ANDROID_APK
export FITCORE_UITEST_ARTIFACTS="${FITCORE_UITEST_ARTIFACTS:-$PWD/TestResults/android}"
mkdir -p "$FITCORE_UITEST_ARTIFACTS"

adb wait-for-device
adb logcat -c
adb logcat '*:W' > "$FITCORE_UITEST_ARTIFACTS/logcat.txt" &
logcat_pid=$!

appium --log "$FITCORE_UITEST_ARTIFACTS/appium.log" --log-no-colors > /dev/null 2>&1 &
appium_pid=$!
trap 'kill "$appium_pid" "$logcat_pid" 2> /dev/null || true' EXIT

for _ in $(seq 1 60); do
  curl -sf http://127.0.0.1:4723/status > /dev/null && break
  sleep 1
done
curl -sf http://127.0.0.1:4723/status > /dev/null || { echo "Appium didn't start; see $FITCORE_UITEST_ARTIFACTS/appium.log" >&2; exit 1; }

dotnet test --project tests/FitCore.Maui.UITests "$@"
