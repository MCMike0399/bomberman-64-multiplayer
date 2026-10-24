#!/usr/bin/env bash
# Optional Switch 2 Pro USB backend. Builds only inside this checkout; no system drivers.
set -euo pipefail
if [[ "$(uname -s)" != Darwin ]]; then
  echo "This bootstrap builds the optional macOS USB backend." >&2
  exit 1
fi
controller_root="$(cd "$(dirname "$0")/../.." && pwd)"
controller_cache="$controller_root/artifacts/controllers"
controller_prefix="$controller_cache/prefix"
controller_output="$controller_root/native/macos"
controller_macos_target=14.0
mkdir -p "$controller_cache" "$controller_prefix" "$controller_output"
fetch() {
  local url="$1" archive="$2" digest="$3"
  if [[ ! -f "$archive" ]]; then curl --fail --location --retry 3 "$url" -o "$archive"; fi
  printf '%s  %s\n' "$digest" "$archive" | shasum -a 256 -c -
}
fetch 'https://github.com/libusb/libusb/releases/download/v1.0.29/libusb-1.0.29.tar.bz2' \
  "$controller_cache/libusb-1.0.29.tar.bz2" '5977fc950f8d1395ccea9bd48c06b3f808fd3c2c961b44b0c2e6e29fc3a70a85'
fetch 'https://github.com/libsdl-org/SDL/releases/download/release-3.4.18/SDL3-3.4.18.tar.gz' \
  "$controller_cache/SDL3-3.4.18.tar.gz" '9c75cf16330322c217dedd2e0609f1124f1b54b8633e763467b4684d0f4334a3'
if [[ ! -x "$controller_cache/.venv/bin/cmake" ]]; then
  python3 -m venv "$controller_cache/.venv"
  "$controller_cache/.venv/bin/pip" install 'cmake==3.31.6'
fi
controller_cmake="$controller_cache/.venv/bin/cmake"
[[ -d "$controller_cache/libusb-1.0.29" ]] || tar -xf "$controller_cache/libusb-1.0.29.tar.bz2" -C "$controller_cache"
tar -xf "$controller_cache/SDL3-3.4.18.tar.gz" -C "$controller_cache"
python3 "$controller_root/tools/controllers/patch-sdl-macos.py" "$controller_cache/SDL3-3.4.18"
python3 "$controller_root/tools/controllers/patch-sdl-hid-reader.py" "$controller_cache/SDL3-3.4.18"
(
  cd "$controller_cache/libusb-1.0.29"
  export MACOSX_DEPLOYMENT_TARGET="$controller_macos_target"
  # New SDKs can link weak pipe2 even when the running/supported OS lacks it.
  # libusb 1.0.29 has no availability guard; use its pipe + fcntl fallback.
  ac_cv_func_pipe2=no ./configure --prefix="$controller_prefix" --disable-static --enable-shared
  make clean
  make -j8
  make install
)
"$controller_cmake" -S "$controller_cache/SDL3-3.4.18" -B "$controller_cache/sdl-3.4.18-build" \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$controller_prefix" \
  -DCMAKE_OSX_DEPLOYMENT_TARGET="$controller_macos_target" \
  -DCMAKE_SHARED_LINKER_FLAGS="-framework GameController" \
  -DSDL_SHARED=ON -DSDL_STATIC=OFF -DSDL_TESTS=OFF -DSDL_TEST_LIBRARY=OFF -DSDL_EXAMPLES=OFF \
  -DSDL_AUDIO=OFF -DSDL_VIDEO=OFF -DSDL_RENDER=OFF -DSDL_GPU=OFF -DSDL_CAMERA=OFF -DSDL_DIALOG=OFF -DSDL_TRAY=OFF \
  -DSDL_HIDAPI=ON -DSDL_HIDAPI_LIBUSB=ON -DSDL_HIDAPI_LIBUSB_SHARED=OFF \
  -DHAVE_FRAMEWORK_GAMECONTROLLER:INTERNAL=FALSE -DHAVE_FRAMEWORK_COREHAPTICS:INTERNAL=FALSE \
  -DLibUSB_LIBRARY="$controller_prefix/lib/libusb-1.0.dylib" \
  -DLibUSB_INCLUDE_PATH="$controller_prefix/include/libusb-1.0"
"$controller_cmake" --build "$controller_cache/sdl-3.4.18-build" --parallel 8
"$controller_cmake" --install "$controller_cache/sdl-3.4.18-build"
cp -L "$controller_prefix/lib/libSDL3.dylib" "$controller_output/libSDL3.dylib"
cp -L "$controller_prefix/lib/libusb-1.0.dylib" "$controller_output/libusb-1.0.dylib"
install_name_tool -id '@rpath/libSDL3.dylib' "$controller_output/libSDL3.dylib"
install_name_tool -id '@loader_path/libusb-1.0.dylib' "$controller_output/libusb-1.0.dylib"
install_name_tool -change "$controller_prefix/lib/libusb-1.0.0.dylib" '@loader_path/libusb-1.0.dylib' "$controller_output/libSDL3.dylib"
install_name_tool -change "$controller_prefix/lib/libusb-1.0.dylib" '@loader_path/libusb-1.0.dylib' "$controller_output/libSDL3.dylib"
codesign --force --sign - "$controller_output/libusb-1.0.dylib"
codesign --force --sign - "$controller_output/libSDL3.dylib"
cp "$controller_cache/SDL3-3.4.18/LICENSE.txt" "$controller_output/SDL-LICENSE.txt"
cp "$controller_cache/libusb-1.0.29/COPYING" "$controller_output/libusb-LICENSE.txt"
otool -L "$controller_output/libSDL3.dylib"
python3 "$controller_root/tools/controllers/check-macos-backend.py" "$controller_output"
echo 'Switch 2 USB backend built. Restart the game to load it.'
