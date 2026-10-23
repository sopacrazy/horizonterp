import sys,pathlib
sys.path.insert(0,str(pathlib.Path('tools/video_libs').resolve()))
import imageio_ffmpeg as ff
from PIL import Image,ImageDraw
r=ff.read_frames(r'C:\Users\Adriano\Desktop\Gravando 2026-10-23 092322.mp4',pix_fmt='rgb24');m=next(r);w,h=m['size'];fps=m['fps'];sheet=Image.new('RGB',(6*220,5*310),(25,25,25));d=ImageDraw.Draw(sheet);n=0
for i,data in enumerate(r):
 t=i/fps
 if t<9:continue
 if t>=11:break
 if i%2:continue
 im=Image.frombytes('RGB',(w,h),data).crop((490,220,800,650));im.thumbnail((220,280));x=n%6*220;y=n//6*310;sheet.paste(im,(x,y));d.text((x,y+283),f'{t:.2f}s',fill='white');n+=1
sheet.save('tools/new-video-run-detail.jpg')
