#!/usr/bin/env python3
"""Keep macOS's HID interface 0 untouched; SDL's Switch 2 driver uses vendor bulk interface 1."""
from pathlib import Path
import sys

path = Path(sys.argv[1]) / "src/hidapi/libusb/hid.c"
text = path.read_text()
marker = "BLAST_SWITCH2_BULK_HANDLE"
if marker in text:
    raise SystemExit(0)
needle = "\tlibusb_get_device_descriptor(libusb_get_device(dev->device_handle), &desc);\n"
assert text.count(needle) == 1, "Pinned SDL source changed; review the USB patch."
replacement = needle + """
#if defined(SDL_PLATFORM_MACOS) && defined(HIDAPI_USING_SDL_RUNTIME)
	/* BLAST_SWITCH2_BULK_HANDLE: macOS owns the HID interface. SDL's dedicated
	   Switch 2 protocol driver claims and uses the separate bulk interface.
	   Supply the open libusb handle without seizing HID or starting its read
	   transfer. No kernel driver is detached for this device. */
	if (desc.idVendor == 0x057e && desc.idProduct == 0x2069) {
		dev->manufacturer_index = desc.iManufacturer;
		dev->product_index = desc.iProduct;
		dev->serial_index = desc.iSerialNumber;
		dev->config_number = conf_desc->bConfigurationValue;
		dev->interface = intf_desc->bInterfaceNumber;
		dev->interface_class = intf_desc->bInterfaceClass;
		dev->interface_subclass = intf_desc->bInterfaceSubClass;
		dev->interface_protocol = intf_desc->bInterfaceProtocol;
		dev->transfer_loop_finished = 1;
		return 1;
	}
#endif
"""
text = text.replace(needle, replacement)
start = text.index("void HID_API_EXPORT hid_close(hid_device *dev)")
prefix, close = text[:start], text[start:]
first = "\tlibusb_cancel_transfer(dev->transfer);\n"
last = "\tlibusb_free_transfer(dev->transfer);\n"
assert close.count(first) == 1 and close.count(last) == 1, "Review SDL close cleanup."
close = close.replace(first, "\tif (dev->transfer) {\n" + first, 1)
close = close.replace(last, last + "\t}\n", 1)
path.write_text(prefix + close)
