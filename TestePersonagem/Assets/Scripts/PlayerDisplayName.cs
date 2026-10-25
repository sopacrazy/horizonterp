public static class PlayerDisplayName
{
    public static string From(string firstName,string fullName=null)
    {
        if(!string.IsNullOrWhiteSpace(firstName))return firstName.Trim();
        var value=(fullName??"").Trim();int space=value.IndexOf(' ');
        return space<0?value:value.Substring(0,space);
    }
    public static string From(ApiPlayerState state)=>From(state?.firstName,state?.name);
}
