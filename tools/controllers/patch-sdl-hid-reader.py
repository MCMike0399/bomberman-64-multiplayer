#!/usr/bin/env python3
"""Use macOS's nonexclusive HID reader for Switch 2 reports alongside the USB initialization handle."""
from pathlib import Path
import sys

path = Path(sys.argv[1]) / "src/hidapi/SDL_hidapi.c"
text = path.read_text()
if "BLAST_SWITCH2_RAW_ENUMERATION" not in text:
    ignored = "        if (RequiresLibUSB(vendor_id, product_id, libusb_xbox)) {\n"
    assert text.count(ignored) == 1, "Review platform enumeration filter."
    text = text.replace(ignored, """        bool requires_usb = RequiresLibUSB(vendor_id, product_id, libusb_xbox);
#if defined(SDL_PLATFORM_MACOS) && defined(HAVE_LIBUSB)
        /* BLAST_SWITCH2_RAW_ENUMERATION: also expose the nonexclusive reader.
           The normal merged enumeration still prefers the USB device. */
        if (vendor_id == 0x057e && product_id == 0x2069) {
            requires_usb = false;
        }
#endif
        if (requires_usb) {
""")
    path.write_text(text)
raw_old = """    if (list && !list->next) {
        raw = PLATFORM_hid_open_path(list->path);
    }"""
raw_new = """    /* BLAST_SWITCH2_USB_READER: never attach a same-model Bluetooth pad. */
    struct hid_device_info *candidate = NULL;
    int matches = 0;
    for (struct hid_device_info *p = list; p; p = p->next) {
        if (p->bus_type == HID_API_BUS_USB) {
            candidate = p;
            ++matches;
        }
    }
    if (matches == 1) {
        raw = PLATFORM_hid_open_path(candidate->path);
    }"""
if "BLAST_SWITCH2_RAW_READER" in text:
    if "BLAST_SWITCH2_USB_READER" not in text:
        assert text.count(raw_old) == 1, "Review raw USB reader selection."
        path.write_text(text.replace(raw_old, raw_new))
    raise SystemExit(0)
anchor = "SDL_hid_device *SDL_hid_open(unsigned short vendor_id, unsigned short product_id, const wchar_t *serial_number)"
assert text.count(anchor) == 1, "Review pinned SDL HID wrapper."
helper = """
/* BLAST_SWITCH2_RAW_READER: vendor interface 1 performs initialization; live
   reports arrive through HID interface 0, opened nonexclusively by macOS.
   Refuse ambiguous multiple devices instead of routing one player's reports
   into another player's gamepad. */
#if defined(SDL_PLATFORM_MACOS) && defined(HAVE_LIBUSB) && defined(HAVE_PLATFORM_BACKEND)
static void SDLCALL BlastCloseSwitch2Raw(void *userdata, void *value)
{
    (void)userdata;
    PLATFORM_hid_close(value);
}
static bool BlastAttachSwitch2Raw(SDL_hid_device *wrapper, void *usb)
{
    struct hid_device_info *info = LIBUSB_hid_get_device_info(usb);
    if (!info || info->vendor_id != 0x057e || info->product_id != 0x2069) {
        return true;
    }
    struct hid_device_info *list = PLATFORM_hid_enumerate(0x057e, 0x2069);
    void *raw = NULL;
    if (list && !list->next) {
        raw = PLATFORM_hid_open_path(list->path);
    }
    PLATFORM_hid_free_enumeration(list);
    if (!raw) {
        SDL_SetError("Switch 2 USB requires one uniquely identified macOS HID reader");
        return false;
    }
    return SDL_SetPointerPropertyWithCleanup(SDL_hid_get_properties(wrapper),
        "blast.switch2.raw", raw, BlastCloseSwitch2Raw, NULL);
}
#endif

"""
text = text.replace(anchor, helper + anchor)
text = text.replace(raw_old, raw_new)
needle = "SDL_SetPointerProperty(SDL_hid_get_properties(dev), SDL_PROP_HIDAPI_LIBUSB_DEVICE_HANDLE_POINTER, ((LIBUSB_hid_device *)pDevice)->device_handle);"
assert text.count(needle) == 2, "Review SDL libusb wrappers."
text = text.replace(needle, needle + """
#if defined(SDL_PLATFORM_MACOS) && defined(HAVE_PLATFORM_BACKEND)
            if (!BlastAttachSwitch2Raw(dev, pDevice)) {
                SDL_hid_close(dev);
                return NULL;
            }
#endif
""")
for method, args in [("hid_read_timeout", "data, length, milliseconds"), ("hid_read", "data, length"), ("hid_set_nonblocking", "nonblock")]:
    line = f"    return device->backend->{method}(device->device, {args});"
    assert text.count(line) == 1, "Review SDL read dispatch."
    text = text.replace(line, f"""
#if defined(SDL_PLATFORM_MACOS) && defined(HAVE_LIBUSB) && defined(HAVE_PLATFORM_BACKEND)
    void *raw = SDL_GetPointerProperty(SDL_hid_get_properties(device), "blast.switch2.raw", NULL);
    if (raw) {{
        return PLATFORM_{method}(raw, {args});
    }}
#endif
{line}""")
path.write_text(text)
