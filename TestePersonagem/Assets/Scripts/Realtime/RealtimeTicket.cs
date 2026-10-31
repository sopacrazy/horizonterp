using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Hbrp.Realtime
{
    [Serializable] public sealed class RealtimeTicketClaims
    {public int v;public string characterId,uid,world,clientId,sessionId,jti;public long exp;}
    // Used by the dedicated server. The signing secret is never a Resources asset or client setting.
    public static class RealtimeTicket
    {
        public static bool Verify(string ticket,string secret,long nowSeconds,out RealtimeTicketClaims claims)
        {
            claims=null;if(string.IsNullOrEmpty(secret)||secret.Length<32||ticket==null||ticket.Length>512)return false;
            try
            {
                var parts=ticket.Split('.');if(parts.Length!=2)return false;
                byte[] signature=Decode(parts[1]),expected;
                using(var hmac=new HMACSHA256(Encoding.UTF8.GetBytes(secret)))expected=hmac.ComputeHash(Encoding.UTF8.GetBytes("hbrp-utp-v1."+parts[0]));
                if(signature.Length!=expected.Length)return false;int mismatch=0;for(int i=0;i<expected.Length;i++)mismatch|=signature[i]^expected[i];
                if(mismatch!=0)return false;
                var c=JsonUtility.FromJson<RealtimeTicketClaims>(Encoding.UTF8.GetString(Decode(parts[0])));
                if(c==null||c.v!=1||c.exp<=nowSeconds||c.exp>nowSeconds+20||c.world!="centro_teste"||
                    !Guid.TryParse(c.characterId,out _)||!Guid.TryParse(c.clientId,out _)||!Guid.TryParse(c.sessionId,out _)||!Guid.TryParse(c.jti,out _)||
                    string.IsNullOrEmpty(c.uid)||c.uid.Length>19||!ulong.TryParse(c.uid,out _))return false;
                claims=c;return true;
            }
            catch(Exception e)when(e is FormatException||e is ArgumentException){return false;}
        }
        static byte[] Decode(string text){text=text.Replace('-','+').Replace('_','/');return Convert.FromBase64String(text+new string('=',(4-text.Length%4)%4));}
    }
}
