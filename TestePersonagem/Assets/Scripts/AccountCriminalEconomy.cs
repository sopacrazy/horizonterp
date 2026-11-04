using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable] public sealed class CriminalEconomyPermissions { public bool deposit, withdraw, craft; }
[Serializable] public sealed class CriminalEconomyOrganization { public string code, name, rank; public CriminalEconomyPermissions permissions; }
[Serializable] public sealed class CriminalEconomyItem { public string itemCode; public int quantity; }
[Serializable] public sealed class CriminalEconomyActivity { public string code, name, reward_item_code; public int reward_quantity, cooldown_seconds, duration_seconds; }
[Serializable] public sealed class CriminalEconomyRecipe { public string code, name, outputItemCode; public int outputQuantity, durationSeconds; public CriminalEconomyItem[] ingredients; }
[Serializable] public sealed class CriminalEconomyProduction { public string productionId, requestId, recipeCode, startedAt, readyAt, status; public int batches; public CriminalEconomyItem output; }
[Serializable] public sealed class CriminalEconomyProductionBody { public string productionId, requestId; }
[Serializable] public sealed class CriminalEconomyLocation
{
    public string code, kind, activityCode, organizationCode, world, label;
    public float x, y, z, radius;
}
[Serializable] public sealed class CriminalEconomyActiveActivity
{
    public string activityRunId, activityCode, startedAt, readyAt, expiresAt;
    public CriminalEconomyLocation location;
}
[Serializable] public sealed class CriminalEconomyState
{
    public CriminalEconomyOrganization organization;
    public CriminalEconomyActivity[] activities;
    public CriminalEconomyItem[] inventory, warehouse;
    public CriminalEconomyRecipe[] recipes;
    public CriminalEconomyLocation[] locations;
    public CriminalEconomyActiveActivity activeActivity;
    public long cashCents;
    public CriminalEconomyShop[] shops;
    public CriminalEconomyProduction activeProduction;
    public string serverTime;
}
[Serializable] public sealed class CriminalEconomyShopStock { public string itemCode; public int quantity, priceCents; }
[Serializable] public sealed class CriminalEconomyShop { public string code, organization, name; public long proceedsCents; public CriminalEconomyShopStock[] stock; }
[Serializable] public sealed class CriminalEconomyCancelBody { public string activityRunId, requestId; }
[Serializable] public sealed class CriminalEconomyShopBody { public string shopCode, itemCode, requestId; public int quantity, priceCents; }
[Serializable] public sealed class CriminalEconomyRecallBody { public string shopCode, itemCode, requestId; public int quantity; }
[Serializable] public sealed class CriminalEconomyProceedsBody { public string shopCode, requestId; public int quantity; }
[Serializable] public sealed class CriminalEconomyPriceBody { public string shopCode, itemCode, requestId; public int priceCents; }
[Serializable] public sealed class CriminalEconomyStartBody { public string activityCode, requestId; }
[Serializable] public sealed class CriminalEconomyCompleteBody { public string activityCode, activityRunId, requestId; }
[Serializable] public sealed class CriminalEconomyTransferBody { public string itemCode, requestId; public int quantity; }
[Serializable] public sealed class CriminalEconomyCraftBody { public string recipeCode, requestId; public int batches; }
[Serializable] public sealed class CriminalEconomyHistoryEntry
{
    public string activityCode, activityRunId, operation, type, kind, itemCode, recipeCode, createdAt, completedAt, startedAt, requestId, shopCode;
    public long totalCents, amountCents, priceCents;
    public int quantity, batches;
    public CriminalEconomyItem reward;
}
[Serializable] public sealed class CriminalEconomyHistory { public CriminalEconomyHistoryEntry[] activities, operations, commerce; }
[Serializable] public sealed class CriminalEconomyMember { public string uid, firstName, rank; }
[Serializable] public sealed class CriminalEconomyAdminOrganization
{
    public string code, name;
    public string[] ranks;
    public CriminalEconomyMember[] members;
}
[Serializable] public sealed class CriminalEconomyAdminOrganizations { public CriminalEconomyAdminOrganization[] organizations; }
[Serializable] public sealed class CriminalEconomyAssignment { public string uid, organizationCode, rankCode; }
public sealed class CriminalEconomyResponse
{
    public string json, code, error;
    public long status;
    public bool stale, uncertain;
    public bool Success => !stale && string.IsNullOrEmpty(error) && string.IsNullOrEmpty(code) && status >= 200 && status < 300;
}
[Serializable] sealed class CriminalEconomyError { public string error, code; }

public sealed partial class AccountSession
{
    // Includes the credential so an old response cannot repopulate a new login.
    internal string CriminalEconomySessionKey => HasCharacter ? token + "|" + State.characterId : null;

    internal IEnumerator CriminalEconomyRequest(string method, string path, string json, Action<CriminalEconomyResponse> done)
    {
        string sessionKey = CriminalEconomySessionKey;
        if (sessionKey == null)
        { done(new CriminalEconomyResponse { code = "UNAUTHORIZED", error = "Entre novamente na sua conta." }); yield break; }
        if (VerificationTransport != null)
        { done(new CriminalEconomyResponse { code = "OFFLINE", error = "A economia persistente exige conexão com o servidor." }); yield break; }
        string requestToken = token;
        using (var request = new UnityWebRequest(config.baseUrl + "/v1/criminal-economy" + path, method))
        {
            request.timeout = 12;
            request.redirectLimit = 0;
            request.certificateHandler = new PinnedCertificate(config.certificateSha256);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", "Bearer " + requestToken);
            if (json != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            yield return request.SendWebRequest();
            if (CriminalEconomySessionKey != sessionKey)
            { done(new CriminalEconomyResponse { stale = true }); yield break; }
            var result = new CriminalEconomyResponse { json = request.downloadHandler.text, status = request.responseCode };
            CriminalEconomyError apiError = null;
            try { apiError = JsonUtility.FromJson<CriminalEconomyError>(result.json); } catch { }
            result.code = apiError?.code;
            result.error = apiError?.error;
            // A 5xx response can follow a committed transaction whose final
            // acknowledgment failed. Preserve its mutation identifier too.
            result.uncertain = request.responseCode == 0 || request.responseCode >= 500;
            if (result.code == "ECONOMY_ADMIN_REQUIRED" && State != null)
            { State.accountRole = "user"; State.adminLevel = 0; }
            if (result.code == "CHARACTER_BLOCKED") InvalidateSession(result.error);
            else if (request.responseCode == 401) yield return ConfirmSessionAfterUnauthorized(requestToken);
            if (CriminalEconomySessionKey != sessionKey)
            { done(new CriminalEconomyResponse { stale = true }); yield break; }
            if (request.result != UnityWebRequest.Result.Success && string.IsNullOrEmpty(result.code))
            {
                result.uncertain = true;
                result.code = "CONNECTION_LOST";
                result.error = "A resposta não chegou. Consulte o estado para confirmar o resultado.";
            }
            if (request.result != UnityWebRequest.Result.Success && string.IsNullOrEmpty(result.error))
                result.error = "Não foi possível concluir a solicitação (" + result.status + ").";
            done(result);
        }
    }
}
