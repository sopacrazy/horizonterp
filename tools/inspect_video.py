import sys,pathlib
sys.path.insert(0,str(pathlib.Path('tools/video_libs').resolve()))
import imageio_ffmpeg as ff
from PIL import Image,ImageDraw
p=r'C:\Users\Adriano\Desktop\teste.mp4'
r=ff.read_frames(p,pix_fmt='rgb24');meta=next(r);print(meta)
w,h=meta['size'];fps=meta['fps'];frames=[]
for i,data in enumerate(r):
 if i%max(1,round(fps))==0:
  im=Image.frombytes('RGB',(w,h),data);im.thumbnail((420,260));frames.append((i/fps,im))
 if len(frames)>=24:break
sheet=Image.new('RGB',(420*4,290*((len(frames)+3)//4)),(30,30,30));d=ImageDraw.Draw(sheet)
for n,(t,im) in enumerate(frames):
 x=n%4*420;y=n//4*290;sheet.paste(im,(x,y));d.text((x+6,y+262),f'{t:.1f}s',fill='white')
sheet.save('tools/user-video-frames.jpg')
