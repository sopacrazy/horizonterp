using System;

// Level 1 is unrestricted. Level 4 is a separate promotion-only role.
// The API checks the same rule independently before any privileged operation.
public static class AdminPermissions
{
    public const int PromotionLevel=4;
    public static bool CurrentCanUseTools=>AccountSession.HasCharacter&&CanUseTools(AccountSession.Instance.State);
    public static bool CanUseTools(ApiPlayerState state)=>state!=null&&state.accountRole=="admin"&&state.adminLevel>0&&state.adminLevel!=PromotionLevel;
    public static bool CanPromote(ApiPlayerState state)=>state!=null&&state.accountRole=="admin"&&(state.adminLevel==1||state.adminLevel==PromotionLevel);

    public static bool CanUseAdminCommand(ApiPlayerState state,string usage,int requiredLevel)
    {
        string command=(usage??"").Trim();
        int separator=command.IndexOfAny(new[]{' ','\t'});
        if(separator>=0)command=command.Substring(0,separator);
        if(command.StartsWith("/",StringComparison.Ordinal))command=command.Substring(1);
        if(state!=null&&state.accountRole=="admin"&&state.adminLevel==1)return true;
        if(string.Equals(command,"adminpromov",StringComparison.OrdinalIgnoreCase))return CanPromote(state);
        return CanUseTools(state)&&requiredLevel<=state.adminLevel;
    }
}
