"""Kaappaa sovelluksen pääikkunan kuviksi PrintWindow-kutsulla.

Käyttö:
    python -I tools/snap-window.py <exe> <out-dir> <sekunnit...>

Käynnistää exen, ottaa kuvan jokaisen annetun sekuntimäärän kohdalla (laskettuna
käynnistyksestä) tiedostoihin <out-dir>/shot1.png, shot2.png, ... ja lopettaa prosessin.

Ikkuna etsitään EnumWindows-kutsulla prosessin pid:n perusteella (näkyvä, ilman
omistajaa). Kuvan koko tulee GetWindowRect-kutsusta, ja bitit luetaan GetDIBits-kutsulla
32-bittisenä BGRA-datana. PrintWindow saa lipun PW_RENDERFULLCONTENT (2), jotta myös
WPF/DirectComposition-sisältö piirtyy. CopyFromScreen ei käy, koska se epäonnistuu
Claude-sessiosta virheellä "Kahva ei kelpaa".

Huom: PC Cleanerin manifesti vaatii requireAdministrator-tason, joten käynnistys
pysähtyy UAC-kehotteeseen. Kaappausta varten vaihda manifestiin väliaikaisesti
asInvoker ja käännä sovellus uudelleen. Tätä muutosta ei commitoida.

Riippuvuudet: vain ctypes (vakiokirjasto) ja Pillow.
"""

import ctypes
import os
import subprocess
import sys
import time
from ctypes import wintypes

PW_RENDERFULLCONTENT = 2
BI_RGB = 0
DIB_RGB_COLORS = 0
GW_OWNER = 4


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", wintypes.DWORD),
        ("biWidth", wintypes.LONG),
        ("biHeight", wintypes.LONG),
        ("biPlanes", wintypes.WORD),
        ("biBitCount", wintypes.WORD),
        ("biCompression", wintypes.DWORD),
        ("biSizeImage", wintypes.DWORD),
        ("biXPelsPerMeter", wintypes.LONG),
        ("biYPelsPerMeter", wintypes.LONG),
        ("biClrUsed", wintypes.DWORD),
        ("biClrImportant", wintypes.DWORD),
    ]


class BITMAPINFO(ctypes.Structure):
    _fields_ = [("bmiHeader", BITMAPINFOHEADER), ("bmiColors", wintypes.DWORD * 3)]


def _win32():
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)

    user32.EnumWindows.argtypes = [ctypes.c_void_p, wintypes.LPARAM]
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetWindowThreadProcessId.restype = wintypes.DWORD
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.GetWindow.argtypes = [wintypes.HWND, wintypes.UINT]
    user32.GetWindow.restype = wintypes.HWND
    user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    user32.GetDC.argtypes = [wintypes.HWND]
    user32.GetDC.restype = wintypes.HDC
    user32.ReleaseDC.argtypes = [wintypes.HWND, wintypes.HDC]
    user32.PrintWindow.argtypes = [wintypes.HWND, wintypes.HDC, wintypes.UINT]
    user32.PrintWindow.restype = wintypes.BOOL

    gdi32.CreateCompatibleDC.argtypes = [wintypes.HDC]
    gdi32.CreateCompatibleDC.restype = wintypes.HDC
    gdi32.CreateCompatibleBitmap.argtypes = [wintypes.HDC, ctypes.c_int, ctypes.c_int]
    gdi32.CreateCompatibleBitmap.restype = wintypes.HBITMAP
    gdi32.SelectObject.argtypes = [wintypes.HDC, wintypes.HGDIOBJ]
    gdi32.SelectObject.restype = wintypes.HGDIOBJ
    gdi32.GetDIBits.argtypes = [wintypes.HDC, wintypes.HBITMAP, wintypes.UINT, wintypes.UINT,
                                ctypes.c_void_p, ctypes.POINTER(BITMAPINFO), wintypes.UINT]
    gdi32.DeleteObject.argtypes = [wintypes.HGDIOBJ]
    gdi32.DeleteDC.argtypes = [wintypes.HDC]
    return user32, gdi32


def find_main_window(user32, pid):
    """Palauttaa prosessin ensimmäisen näkyvän, omistajattoman päätason ikkunan tai None."""
    found = []
    proc_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

    def callback(hwnd, _lparam):
        owner_pid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(owner_pid))
        if (owner_pid.value == pid and user32.IsWindowVisible(hwnd)
                and not user32.GetWindow(hwnd, GW_OWNER)):
            found.append(hwnd)
            return False
        return True

    cb = proc_type(callback)
    user32.EnumWindows(cb, 0)
    return found[0] if found else None


def capture(user32, gdi32, hwnd, path):
    from PIL import Image

    rect = wintypes.RECT()
    if not user32.GetWindowRect(hwnd, ctypes.byref(rect)):
        raise ctypes.WinError(ctypes.get_last_error())
    width, height = rect.right - rect.left, rect.bottom - rect.top
    if width <= 0 or height <= 0:
        raise RuntimeError(f"ikkunan koko on {width}x{height}")

    screen_dc = user32.GetDC(None)
    mem_dc = gdi32.CreateCompatibleDC(screen_dc)
    bitmap = gdi32.CreateCompatibleBitmap(screen_dc, width, height)
    old = gdi32.SelectObject(mem_dc, bitmap)
    try:
        if not user32.PrintWindow(hwnd, mem_dc, PW_RENDERFULLCONTENT):
            raise RuntimeError("PrintWindow epäonnistui")
        info = BITMAPINFO()
        info.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
        info.bmiHeader.biWidth = width
        info.bmiHeader.biHeight = -height  # negatiivinen = ylhäältä alas
        info.bmiHeader.biPlanes = 1
        info.bmiHeader.biBitCount = 32
        info.bmiHeader.biCompression = BI_RGB
        buf = ctypes.create_string_buffer(width * height * 4)
        lines = gdi32.GetDIBits(mem_dc, bitmap, 0, height, buf, ctypes.byref(info), DIB_RGB_COLORS)
        if lines != height:
            raise RuntimeError(f"GetDIBits palautti {lines} riviä, odotettiin {height}")
    finally:
        gdi32.SelectObject(mem_dc, old)
        gdi32.DeleteObject(bitmap)
        gdi32.DeleteDC(mem_dc)
        user32.ReleaseDC(None, screen_dc)

    img = Image.frombuffer("RGBA", (width, height), buf.raw, "raw", "BGRA", 0, 1)
    # GDI jättää alfakanavan usein nollaksi, joten kuva tallennetaan läpinäkymättömänä.
    img.convert("RGB").save(path)


def main(argv):
    if len(argv) < 3 or argv[0] in ("-h", "--help"):
        print(__doc__)
        return 0 if argv and argv[0] in ("-h", "--help") else 2

    exe, out_dir = argv[0], argv[1]
    try:
        times = [float(t) for t in argv[2:]]
    except ValueError:
        print("virhe: sekuntimäärien pitää olla lukuja", file=sys.stderr)
        return 2

    user32, gdi32 = _win32()
    os.makedirs(out_dir, exist_ok=True)
    proc = subprocess.Popen([exe])
    start = time.monotonic()
    try:
        for n, t in enumerate(times, 1):
            delay = start + t - time.monotonic()
            if delay > 0:
                time.sleep(delay)
            if proc.poll() is not None:
                print(f"virhe: prosessi päättyi koodilla {proc.returncode}", file=sys.stderr)
                return 1
            hwnd = find_main_window(user32, proc.pid)
            if not hwnd:
                print(f"virhe: pid {proc.pid}: näkyvää pääikkunaa ei löytynyt ({t} s)", file=sys.stderr)
                return 1
            path = os.path.join(out_dir, f"shot{n}.png")
            capture(user32, gdi32, hwnd, path)
            print(f"{path} ({t} s)")
    finally:
        if proc.poll() is None:
            proc.kill()
            proc.wait()
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
