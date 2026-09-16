# 生成 EasyBIM2Rhino.rui（Rhino 工具栏/图标按钮）
# 图标来源：优先 assets/export.png 与 assets/import.png（任意尺寸、带透明通道）；
#           缺省时用占位纯色方块。
# 生成后请在 Rhino 工具栏中显示该工具栏；图标更新流程：替换 assets 下 png → 重跑本脚本 → 重编 → 重启 Rhino。
import zlib, struct, base64, uuid, os

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, 'assets')

# ---------- 纯 Python PNG 读取（8bit, 非隔行, RGB/RGBA） ----------
def read_png(path):
    with open(path, 'rb') as f:
        data = f.read()
    assert data[:8] == b'\x89PNG\r\n\x1a\n', 'not a png'
    pos = 8; idat = b''; w = h = None; color = None
    while pos < len(data):
        ln = struct.unpack('>I', data[pos:pos + 4])[0]
        tag = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + ln]
        if tag == b'IHDR':
            w, h, depth, color, comp, filt, inter = struct.unpack('>IIBBBBB', body)
            assert depth == 8 and inter == 0 and color in (2, 6), 'unsupported png format'
        elif tag == b'IDAT':
            idat += body
        pos += 12 + ln
    raw = zlib.decompress(idat)
    ch = 4 if color == 6 else 3
    stride = w * ch
    out = []
    prev = bytearray(stride)
    p = 0
    for y in range(h):
        ft = raw[p]; p += 1
        line = bytearray(raw[p:p + stride]); p += stride
        if ft == 1:
            for i in range(ch, stride): line[i] = (line[i] + line[i - ch]) & 0xff
        elif ft == 2:
            for i in range(stride): line[i] = (line[i] + prev[i]) & 0xff
        elif ft == 3:
            for i in range(stride):
                a = line[i - ch] if i >= ch else 0
                line[i] = (line[i] + ((a + prev[i]) >> 1)) & 0xff
        elif ft == 4:
            for i in range(stride):
                a = line[i - ch] if i >= ch else 0
                b = prev[i]
                c = prev[i - ch] if i >= ch else 0
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 0xff
        out.append(bytes(line))
        prev = line
    pix = []
    for y in range(h):
        row = out[y]
        for x in range(w):
            o = x * ch
            if ch == 4:
                pix.append((row[o], row[o + 1], row[o + 2], row[o + 3]))
            else:
                pix.append((row[o], row[o + 1], row[o + 2], 255))
    return w, h, pix

def resample(pix, w, h, n):
    """box 平均降采样到 n x n（RGBA）"""
    out = []
    for ty in range(n):
        for tx in range(n):
            r = g = b = a = 0.0; cnt = 0
            y0, y1 = ty * h // n, (ty + 1) * h // n
            x0, x1 = tx * w // n, (tx + 1) * w // n
            for sy in range(y0, max(y0 + 1, y1)):
                for sx in range(x0, max(x0 + 1, x1)):
                    pr, pg, pb, pa = pix[sy * w + sx]
                    r += pr; g += pg; b += pb; a += pa; cnt += 1
            cnt = max(1, cnt)
            out.append((int(r / cnt), int(g / cnt), int(b / cnt), int(a / cnt)))
    return out

def placeholder(n, color):
    return [color + (255,)] * (n * n)

# ---------- PNG 写出（模仿 Rhino default.rui 的编码：gAMA+pHYs 辅助块、IDAT 分块，兼容 R7 老解码器） ----------
def png_bytes(w, h, pixels):
    raw = b''
    for y in range(h):
        raw += b'\x00'
        for x in range(w):
            raw += bytes(pixels[y * w + x])
    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff)
    o = b'\x89PNG\r\n\x1a\n'
    o += chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
    o += chunk(b'gAMA', struct.pack('>I', 45455))
    o += chunk(b'pHYs', struct.pack('>IIB', 3780, 3780, 1))
    comp = zlib.compress(raw, 9)
    pos = 0
    while pos < len(comp):
        o += chunk(b'IDAT', comp[pos:pos + 65524])
        pos += 65524
    o += chunk(b'IEND', b'')
    return o

def icon_at(path, n, fallback_color):
    if os.path.exists(path):
        w, h, pix = read_png(path)
        return resample(pix, w, h, n)
    return placeholder(n, fallback_color)

def strip(icons, n):
    pix = []
    for y in range(n):
        for ic in icons:
            for x in range(n):
                pix.append(ic[y * n + x])
    return png_bytes(n * len(icons), n, pix)

G_ROOT = str(uuid.uuid4()); G_TB = str(uuid.uuid4()); G_GRP = str(uuid.uuid4())
G_M1 = str(uuid.uuid4()); G_M2 = str(uuid.uuid4())
G_I1 = str(uuid.uuid4()); G_I2 = str(uuid.uuid4())
B1 = str(uuid.uuid4()); B2 = str(uuid.uuid4())

EXP = os.path.join(ASSETS, 'EBExport.png')
IMP = os.path.join(ASSETS, 'ImportEB.png')

sizes = [(16, 'small_bitmap'), (24, 'normal_bitmap'), (32, 'large_bitmap')]
bitmaps_xml = []
for n, tag in sizes:
    de = icon_at(EXP, n, (150, 150, 150))
    di = icon_at(IMP, n, (120, 120, 120))
    b64 = base64.b64encode(strip([de, di], n)).decode()
    bitmaps_xml.append(
        f'    <{tag} item_width="{n}" item_height="{n}">\n'
        f'      <bitmap_item guid="{B1}" index="0" />\n'
        f'      <bitmap_item guid="{B2}" index="1" />\n'
        f'      <bitmap>{b64}</bitmap>\n'
        f'    </{tag}>'
    )

def txt(zh, en):
    return f'<text><locale_2052>{zh}</locale_2052><locale_1033>{en}</locale_1033></text>'

rui = f'''<?xml version="1.0" encoding="utf-8"?>
<RhinoUI major_ver="3" minor_ver="0" guid="{G_ROOT}" localize="False" default_language_id="2052" dpi_scale="100">
  <extend_rhino_menus />
  <macros>
    <macro_item guid="{G_M1}" bitmap_id="{B1}">
      {txt('导出到EasyBIM', 'Export to EasyBIM')}
      <script>! _EBModelExport</script>
    </macro_item>
    <macro_item guid="{G_M2}" bitmap_id="{B2}">
      {txt('从EasyBIM导入', 'Import from EasyBIM')}
      <script>! _ImportEBModel</script>
    </macro_item>
  </macros>
  <tool_bars>
    <tool_bar guid="{G_TB}" bitmap_id="{B1}">
      {txt('EasyBIM2Rhino', 'EasyBIM2Rhino')}
      <tool_bar_item guid="{G_I1}">
        {txt('导出到EasyBIM', 'Export to EasyBIM')}
        <left_macro_id>{G_M1}</left_macro_id>
      </tool_bar_item>
      <tool_bar_item guid="{G_I2}">
        {txt('从EasyBIM导入', 'Import from EasyBIM')}
        <left_macro_id>{G_M2}</left_macro_id>
      </tool_bar_item>
    </tool_bar>
  </tool_bars>
  <tool_bar_groups>
    <tool_bar_group guid="{G_GRP}" single_file="True" hide_single_tab="False">
      {txt('EasyBIM2Rhino', 'EasyBIM2Rhino')}
      <tool_bar_id>{G_TB}</tool_bar_id>
    </tool_bar_group>
  </tool_bar_groups>
  <bitmaps>
{chr(10).join(bitmaps_xml)}
  </bitmaps>
</RhinoUI>
'''

# 同时写 R8 与 R7 两个槽位（同一份内容）；若 R7 解码不了本条带，
# 则改为手工维护 Toolbars\Rhino7\（R7 编辑器产出），并把下面第二行注释掉。
_outs = [
    os.path.join(HERE, 'EasyBIM2Rhino', 'Toolbars', 'Rhino8', 'EasyBIM2Rhino.rui'),
    os.path.join(HERE, 'EasyBIM2Rhino', 'Toolbars', 'Rhino7', 'EasyBIM2Rhino.rui'),
]
for _out in _outs:
    with open(_out, 'w', encoding='utf-8') as f:
        f.write(rui)
    print('written', _out, 'bytes =', len(rui.encode('utf-8')),
          '| assets export:', os.path.exists(EXP), 'import:', os.path.exists(IMP))
