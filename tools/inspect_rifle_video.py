import sys,pathlib
sys.path.insert(0,str(pathlib.Path('tools/video_libs').resolve()))
import imageio_ffmpeg as ff
from PIL import Image,ImageDraw
p=pathlib.Path(r'C:\Users\Adriano\AppData\Local\Packages\Microsoft.ScreenSketch_8wekyb3d8bbwe\TempState\Recordings\20261102-1933-59.4884309.mp4')
r=ff.read_frames(str(p),pix_fmt='rgb24');m=next(r);print(m)
w,h=m['size'];fps=m['fps'];frames=[]
for i,data in enumerate(r):
 if i%max(1,round(fps*.5))==0:
  im=Image.frombytes('RGB',(w,h),data)
  im=im.crop((int(w*.28),int(h*.20),int(w*.72),int(h*.98)))
  im.thumbnail((250,320));frames.append((i/fps,im))
 if len(frames)>=48:break
out=pathlib.Path('tools/rifle-video-review');out.mkdir(exist_ok=True)
for page in range((len(frames)+23)//24):
 subset=frames[page*24:(page+1)*24]
 sheet=Image.new('RGB',(250*6,345*4),(25,25,25));d=ImageDraw.Draw(sheet)
 for n,(t,im) in enumerate(subset):
  x=n%6*250;y=n//6*345;sheet.paste(im,(x,y));d.text((x+4,y+322),f'{t:.2f}s',fill='white')
 sheet.save(out/f'sheet-{page}.jpg')
