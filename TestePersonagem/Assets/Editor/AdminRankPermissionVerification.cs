using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Pure permission checks: no scene, account, Play Mode or network is created.
public static class AdminRankPermissionVerification
{
    [Serializable] sealed class Result
    {
        public bool passed;
        public List<string> checks=new List<string>();
    }
    [MenuItem("Horizonte/Testes locais/Validar cargo administrativo nível 4")]
    public static void Run()
    {
        var result=new Result();
        void Check(bool condition,string description)
        {if(!condition)throw new InvalidOperationException(description);result.checks.Add(description);}
        Check(!AdminPermissions.CanUseTools(null)&&!AdminPermissions.CanPromote(null),"No state grants no administrative permissions");
        var player=new ApiPlayerState {accountRole="user",adminLevel=0};
        Check(!AdminPermissions.CanUseTools(player)&&!AdminPermissions.CanPromote(player),"Ordinary players have neither admin tools nor promotion");
        player.adminLevel=4;
        Check(!AdminPermissions.CanUseTools(player)&&!AdminPermissions.CanPromote(player),"A level number without the admin role grants no access");
        foreach(int level in new[]{1,2,3})
        {
            var admin=new ApiPlayerState {accountRole="admin",adminLevel=level};
            Check(AdminPermissions.CanUseTools(admin),"Existing admin tools remain available to level "+level);
            Check(!AdminPermissions.CanPromote(admin)&&!AdminPermissions.CanUseAdminCommand(admin,"/adminpromov [ID/nick] [1|2]",4),"Level "+level+" cannot use the exclusive promotion command");
            Check(AdminPermissions.CanUseAdminCommand(admin,"/speed [1|2|4]",1),"Existing speed access remains available to level "+level);
        }
        var promoter=new ApiPlayerState {accountRole="admin",adminLevel=4};
        Check(AdminPermissions.CanPromote(promoter)&&!AdminPermissions.CanUseTools(promoter),"Level 4 can promote but cannot use the admin tools, speed, teleport or police shortcut");
        Check(AdminPermissions.CanUseAdminCommand(promoter,"/adminpromov [ID/nick] [1|2]",4),"The promotion command is available to level 4");
        Check(AdminPermissions.CanUseAdminCommand(promoter,"ADMINPROMOV",4),"Promotion command recognition is case insensitive");
        foreach(string command in new[]{"/speed 12","/goto 55","/helpadmin","/makeadmin 55 3","/apanel","/a texto","/t 10"})
            Check(!AdminPermissions.CanUseAdminCommand(promoter,command,1),"Level 4 cannot inherit "+command);
        Check(!AdminPermissions.CanUseAdminCommand(promoter,"/adminpromovother",4),"A similar command name cannot bypass the restricted role");
        var future=new ApiPlayerState {accountRole="admin",adminLevel=5};
        Check(!AdminPermissions.CanPromote(future)&&!AdminPermissions.CanUseAdminCommand(future,"/adminpromov 55 2",4),"Promotion is exclusive to level 4, not inherited by a higher number");
        result.passed=true;
        string folder=Path.GetFullPath("../tools/admin-rank4");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"unity-permission-validation.json"),JsonUtility.ToJson(result,true));
        Debug.Log("ADMIN_RANK4_PERMISSIONS_VERIFIED checks="+result.checks.Count);
    }
}
