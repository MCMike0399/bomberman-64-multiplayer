# Peer transport dependency

Run `python3 tools/networking/bootstrap_webrtc.py` before importing a fresh Godot
checkout. QA and packaging do this automatically. The installer verifies the
entire official 1.2.2-stable ZIP against its SHA-256 before installing the native
extension under ignored `addons/webrtc_native/`. It also repairs modified local
copies from the verified archive.

The bootstrap also adds the verified extension to the ignored
`.godot/extension_list.cfg`, preserving other entries. A cold Linux Godot 4.7.2
import reproduced a crash when discovering the extension during import; loading
it at startup avoids that path. Keep the strict import exit/error check. Do not
turn a failed first import into an accepted retry. The minimal native reproduction
and its successful preloaded counterpart are retained with the v0.27 receipts.

The [official release](https://github.com/godotengine/webrtc-native/releases/tag/1.2.2-stable)
and [corresponding source/submodules](https://github.com/godotengine/webrtc-native/tree/1.2.2-stable)
provide the MIT extension, MPL-2.0 libdatachannel and BSD usrsctp. Package notices
include those licenses and source/rebuild references. No private dependency is
involved in this transport.

Update version and hash together, then run installer contracts, the managed
network suite, `peer-rooms` on a real native extension, and the package gate.
The package and headless CI gates exercise actual direct WebRTC gameplay,
relay fallback, host transfer and eight-player data delivery.
