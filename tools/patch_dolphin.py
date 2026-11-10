import zipfile, hashlib, zlib, struct, sys
src, out = sys.argv[1], sys.argv[2]
z = zipfile.ZipFile(src)
dex = bytearray(z.read('classes.dex'))
OFF = 0x4129c
assert dex[OFF:OFF + 4] == bytes.fromhex('39030d00'), dex[OFF:OFF + 4].hex()
dex[OFF:OFF + 4] = b'\x00\x00\x00\x00'  # the on-screen buttons start hidden (no touchscreen on a TV)
dex[12:32] = hashlib.sha1(dex[32:]).digest()
struct.pack_into('<I', dex, 8, zlib.adler32(bytes(dex[12:])) & 0xffffffff)
with zipfile.ZipFile(out, 'w') as o:
    for i in z.infolist():
        if i.filename.startswith('META-INF/'): continue
        data = bytes(dex) if i.filename == 'classes.dex' else z.read(i.filename)
        ni = zipfile.ZipInfo(i.filename, i.date_time); ni.external_attr = i.external_attr
        ni.compress_type = zipfile.ZIP_STORED if i.filename.endswith(('.so', '.arsc')) or i.compress_type == zipfile.ZIP_STORED else zipfile.ZIP_DEFLATED
        o.writestr(ni, data)
print('patched')
