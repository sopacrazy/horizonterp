import sys,pathlib
sys.path.insert(0,str(pathlib.Path('tools/video_libs').resolve()))
from PIL import Image,ImageDraw
for gait in ['walk','run']:
 files=sorted(pathlib.Path('tools/review-'+gait).glob('*.png'))
 sheet=Image.new('RGB',(4*320,3*260),(25,25,25));d=ImageDraw.Draw(sheet)
 for n,i in enumerate(range(0,60,5)):
  im=Image.open(files[i]);im=im.crop((180,60,450,440));im.thumbnail((320,230));x=n%4*320;y=n//4*260;sheet.paste(im,(x,y));d.text((x+3,y+235),str(round(i/30,2))+'s',fill='white')
 sheet.save('tools/review-'+gait+'-sheet.jpg')
