import os,pty,sys,fcntl,termios,select,time
rows,cols=int(sys.argv[1]),int(sys.argv[2])
import struct
pid,fd=pty.fork()
if pid==0:
    fcntl.ioctl(0,termios.TIOCSWINSZ,struct.pack("HHHH",rows,cols,0,0))
    os.execvp("btop",["btop"])
fcntl.ioctl(fd,termios.TIOCSWINSZ,struct.pack("HHHH",rows,cols,0,0))
out=b""; t=time.time()
while time.time()-t<3:
    r,_,_=select.select([fd],[],[],0.4)
    if r:
        try: d=os.read(fd,8192)
        except OSError: break
        if not d: break
        out+=d
try: os.kill(pid,9)
except Exception: pass
txt=out.decode("utf8","replace")
hits=[l.strip() for l in txt.replace("\x1b","\n").split("\n") if "rror" in l or "small" in l.lower() or "Resize" in l]
print(f"{cols}x{rows}: bytes={len(out):6d}  {hits[:2]}")
