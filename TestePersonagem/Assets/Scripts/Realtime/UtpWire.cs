using System;
using System.Text;
using Unity.Collections;
using UnityEngine;

namespace Hbrp.Realtime
{
    public enum PacketKind:byte {Authenticate=1,Authenticated=2,Move=3,Snapshot=4,Ping=5,Pong=6,WeaponMove=7,WeaponSnapshot=8}
    // Cosmetic only: this never carries hit results, damage, inventory or ammunition authority.
    public struct UdpWeaponPose
    {
        public string CharacterId;
        public int Sequence,ShotSequence;
        public long UpdatedAt;
        public double ReceivedAt;
        public bool Equipped,Aiming;
        public float Pitch;
    }
    public struct UdpPose
    {
        public string CharacterId;
        public int Sequence;
        public long UpdatedAt,ServerTime;
        public double ReceivedAt;
        public Vector3 Position;
        public float Yaw,Speed,JumpPhase;
        public bool Jumping,Driving;
    }
    public static class UtpWire
    {
        const uint Magic=0x48525031;
        public static long Now=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        public static void Header(ref DataStreamWriter writer,PacketKind kind){writer.WriteUInt(Magic);writer.WriteByte(1);writer.WriteByte((byte)kind);}
        public static bool Header(ref DataStreamReader reader,out PacketKind kind)
        {
            kind=0;if(reader.Length<6||reader.ReadUInt()!=Magic||reader.ReadByte()!=1)return false;
            kind=(PacketKind)reader.ReadByte();return true;
        }
        public static void Text(ref DataStreamWriter writer,string text)
        {
            byte[] bytes=Encoding.UTF8.GetBytes(text);if(bytes.Length>512)throw new ArgumentException("Protocol string too long");
            writer.WriteUShort((ushort)bytes.Length);foreach(byte value in bytes)writer.WriteByte(value);
        }
        public static string Text(ref DataStreamReader reader)
        {
            if(reader.Length-reader.GetBytesRead()<2)throw new FormatException();
            int size=reader.ReadUShort();if(size>512||reader.Length-reader.GetBytesRead()<size)throw new FormatException();
            var bytes=new byte[size];for(int i=0;i<size;i++)bytes[i]=reader.ReadByte();return Encoding.UTF8.GetString(bytes);
        }
        public static void Pose(ref DataStreamWriter w,UdpPose p)
        {
            w.WriteInt(p.Sequence);w.WriteFloat(p.Position.x);w.WriteFloat(p.Position.y);w.WriteFloat(p.Position.z);
            w.WriteFloat(p.Yaw);w.WriteFloat(p.Speed);w.WriteFloat(p.JumpPhase);w.WriteByte((byte)((p.Jumping?1:0)|(p.Driving?2:0)));
        }
        public static UdpPose Pose(ref DataStreamReader r)
        {
            if(r.Length-r.GetBytesRead()<29)throw new FormatException();
            var p=new UdpPose{Sequence=r.ReadInt(),Position=new Vector3(r.ReadFloat(),r.ReadFloat(),r.ReadFloat()),Yaw=r.ReadFloat(),Speed=r.ReadFloat(),JumpPhase=r.ReadFloat()};
            byte flags=r.ReadByte();if(flags>3)throw new FormatException();p.Jumping=(flags&1)!=0;p.Driving=(flags&2)!=0;
            if(!Valid(p))throw new FormatException();return p;
        }
        public static bool Valid(UdpPose p)
        {
            return p.Sequence>=0&&Finite(p.Position.x)&&Finite(p.Position.y)&&Finite(p.Position.z)&&Finite(p.Yaw)&&Finite(p.Speed)&&Finite(p.JumpPhase)&&
                p.Position.x>=-4200&&p.Position.x<=4200&&p.Position.y>=-200&&p.Position.y<=1000&&p.Position.z>=-4200&&p.Position.z<=4200&&
                p.Yaw>=0&&p.Yaw<360&&p.Speed>=0&&p.Speed<=8&&p.JumpPhase>=0&&p.JumpPhase<=1;
        }
        public static void Weapon(ref DataStreamWriter w,UdpWeaponPose p)
        {w.WriteInt(p.Sequence);w.WriteInt(p.ShotSequence);w.WriteFloat(p.Pitch);w.WriteByte((byte)((p.Equipped?1:0)|(p.Aiming?2:0)));}
        public static UdpWeaponPose Weapon(ref DataStreamReader r)
        {
            if(r.Length-r.GetBytesRead()<13)throw new FormatException();
            var p=new UdpWeaponPose {Sequence=r.ReadInt(),ShotSequence=r.ReadInt(),Pitch=r.ReadFloat()};
            byte flags=r.ReadByte();if(flags>3)throw new FormatException();p.Equipped=(flags&1)!=0;p.Aiming=(flags&2)!=0;
            if(!Valid(p))throw new FormatException();return p;
        }
        public static bool Valid(UdpWeaponPose p)=>p.Sequence>=0&&p.ShotSequence>=0&&Finite(p.Pitch)&&p.Pitch>=-80&&p.Pitch<=80&&(!p.Aiming||p.Equipped);
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
