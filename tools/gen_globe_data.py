import json, numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import distance_transform_edt as edt
W,H=3072,1536
def px(lon,lat): return ((lon+180)/360*W, (90-lat)/180*H)
def load(n): return json.load(open(f'C:/rv/_ne/{n}.geojson',encoding='utf-8'))['features']
def polys(g):
    if g['type']=='Polygon': return [g['coordinates']]
    if g['type']=='MultiPolygon': return g['coordinates']
    return []
def lines(g):
    if g['type']=='LineString': return [g['coordinates']]
    if g['type']=='MultiLineString': return g['coordinates']
    return []
land=Image.new('L',(W,H),0); d=ImageDraw.Draw(land)
for f in load('ne_50m_land'):
    for p in polys(f['geometry']):
        d.polygon([px(*c[:2]) for c in p[0]],fill=255)
        for hole in p[1:]: d.polygon([px(*c[:2]) for c in hole],fill=0)
for f in load('ne_50m_lakes'):
    for p in polys(f['geometry']):
        if len(p[0])>6: d.polygon([px(*c[:2]) for c in p[0]],fill=0)
L=np.array(land)>127
s=edt(L)-edt(~L)
land_b=np.clip(128+s*16,0,255).astype(np.uint8)
def linemask(name):
    im=Image.new('L',(W,H),0); dd=ImageDraw.Draw(im)
    for f in load(name):
        for l in lines(f['geometry']):
            dd.line([px(*c[:2]) for c in l],fill=255,width=1)
    m=np.array(im)>0
    return np.clip(edt(~m)*16,0,255).astype(np.uint8)
ctry=linemask('ne_50m_admin_0_boundary_lines_land'); st=linemask('ne_50m_admin_1_states_provinces_lines')
open('C:/rv/Assets/Resources/GlobeData.bytes','wb').write(land_b.tobytes()+ctry.tobytes()+st.tobytes())
# flat map used by the weather pages
sm=np.clip(128+s*16,0,255)/255.0
a=np.clip((sm-0.5)*8+0.5,0,1)[...,None]
ocean=np.array([30,100,180],float); lnd=np.array([98,160,74],float)
yy=np.linspace(0,1,H)[:,None,None]
oc=ocean*(0.9+0.1*np.sin(yy*3.14))
img=(oc*(1-a)+lnd*a)
bd=np.clip(1-ctry/20.0,0,1)[...,None]*0.5
img=img*(1-bd)+255*bd
Image.fromarray(img.astype(np.uint8)).resize((2048,1024),Image.LANCZOS).save('C:/rv/Assets/Resources/WeatherMap.png')
print('ok', land_b.shape)
