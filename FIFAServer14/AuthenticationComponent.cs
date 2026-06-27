using Blaze.Core;
using Blaze3SDK.Blaze;
using Blaze3SDK.Blaze.Authentication;
using Blaze3SDK.Blaze.CensusData;
using Blaze3SDK.Components;
using EATDF.Types;

namespace FIFAServer14;

internal sealed class AuthenticationComponent : AuthenticationComponentBase.Server
{
    private const long UserId = 1000;
    private const string PersonaName = "FUT14";
    private const string Email = "player@fifa14.local";
    private const string SessionKey = "fifa14sessionkey";
    private const uint Locale = 1701729619;

    public override Task<LoginResponse> LoginAsync(LoginRequest request, BlazeRpcContext context)
    {
        Console.WriteLine($"[Auth] Login: {request.Email}");
        return Task.FromResult(new LoginResponse
        {
            UserId = UserId,
            SessionKey = SessionKey,
            NeedsLegalDoc = false,
            IsOfLegalContactAge = true,
            PCLoginToken = "fifa14token",
            PersonaDetailsList = new List<PersonaDetails> { MakePersona() },
        });
    }

    public override Task<FullLoginResponse> SilentLoginAsync(SilentLoginRequest request, BlazeRpcContext context)
    {
        Console.WriteLine("[Auth] SilentLogin");
        return Task.FromResult(MakeFullLoginResponse("fifa14token"));
    }

    public override Task<FullLoginResponse> OriginLoginAsync(OriginLoginRequest request, BlazeRpcContext context)
    {
        Console.WriteLine($"[Auth] OriginLogin token: {request.AuthToken}");
        var connection = (BlazeRpcConnection)context.Connection;
        var response = MakeFullLoginResponse(request.AuthToken ?? "fifa14token");

        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            await PushUserSessionAsync(connection);
        });

        return Task.FromResult(response);
    }

    public override Task<FullLoginResponse> ExpressLoginAsync(ExpressLoginRequest request, BlazeRpcContext context)
    {
        Console.WriteLine($"[Auth] ExpressLogin: {request.Email}");
        var connection = (BlazeRpcConnection)context.Connection;

        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            await PushUserSessionAsync(connection);
        });

        return Task.FromResult(MakeFullLoginResponse("fifa14token"));
    }

    public override async Task<SessionInfo> LoginPersonaAsync(LoginPersonaRequest request, BlazeRpcContext context)
    {
        Console.WriteLine($"[Auth] LoginPersona: {request.PersonaName}");
        var connection = (BlazeRpcConnection)context.Connection;
        var session = MakeSession();

        await UserSessionsBase.Server.NotifyUserSessionExtendedDataUpdateAsync(connection,
            new UserSessionExtendedDataUpdate
            {
                ExtendedData = new UserSessionExtendedData
                {
                    BestPingSiteAlias = "gva",
                    Country = "GB",
                    HardwareFlags = HardwareFlags.None,
                    LatencyList = new List<int>(),
                    BlazeObjectIdList = new List<ObjectId>(),
                    ClientAttributes = new Dictionary<uint, int>(),
                    DataMap = new Dictionary<uint, long>(),
                },
                UserId = UserId,
            });

        await CensusDataComponentBase.Server.NotifyNotifyServerCensusDataAsync(connection,
            new NotifyServerCensusData { CensusDataList = new List<NotifyServerCensusDataItem>() });

        return session;
    }

    // Pushes the post-login user-session notifications the client expects, in the same order
    // Zamboni3 fires them from ServerManager.AddServerPlayer (all sent immediately):
    //   1. UserSessions::UserAuthenticated   (hand-rolled; absent from this SDK)
    //   2. UserSessions::UserAdded
    //   3. UserSessions::UserSessionExtendedDataUpdate
    //   4. CensusData::ServerCensusData      (advisor flagged this one too)
    // Without these the client times out after sign-in.
    private static async Task PushUserSessionAsync(BlazeRpcConnection connection)
    {
        Console.WriteLine("[Notify] PushUserSession START");
        await Send("UserAuthenticated", () =>
            UserSessionsNotifications.SendUserAuthenticatedAsync(connection, UserId, subscribed: true));

        await Send("UserAdded", () => UserSessionsBase.Server.NotifyUserAddedAsync(connection,
            new NotifyUserAdded
            {
                ExtendedData = new UserSessionExtendedData(),
                UserInfo = new UserIdentification
                {
                    AccountId = UserId,
                    AccountLocale = Locale,
                    BlazeId = UserId,
                    ExternalId = 0,
                    Name = PersonaName,
                },
            }, sendNow: true));

        await Send("ExtendedDataUpdate", () => UserSessionsBase.Server.NotifyUserSessionExtendedDataUpdateAsync(connection,
            new UserSessionExtendedDataUpdate
            {
                ExtendedData = new UserSessionExtendedData(),
                UserId = UserId,
            }, sendNow: true));

        await Send("ServerCensusData", () => CensusDataComponentBase.Server.NotifyNotifyServerCensusDataAsync(connection,
            new NotifyServerCensusData { CensusDataList = new List<NotifyServerCensusDataItem>() }));
        Console.WriteLine("[Notify] PushUserSession DONE");
    }
    private static async Task Send(string name, Func<Task> send)
    {
        try { await send(); Console.WriteLine($"[Notify] sent {name}"); }
        catch (Exception ex) { Console.WriteLine($"[Notify] FAILED {name}: {ex.GetType().Name}: {ex.Message}"); }
    }

    public override Task<EmptyMessage> LogoutPersonaAsync(EmptyMessage request, BlazeRpcContext context)
    {
        Console.WriteLine("[Auth] LogoutPersona");
        return Task.FromResult(new EmptyMessage());
    }

    public override Task<EmptyMessage> LogoutAsync(EmptyMessage request, BlazeRpcContext context)
    {
        Console.WriteLine("[Auth] Logout");
        return Task.FromResult(new EmptyMessage());
    }

    public override Task<GetTosInfoResponse> GetTosInfoAsync(GetTosInfoRequest request, BlazeRpcContext context)
        => Task.FromResult(new GetTosInfoResponse
        {
            EaMayContact = 0,
            PartnersMayContact = 0,
            PrivacyPolicyUri = "",
            TosHost = "",
            TosUri = "",
        });

    public override Task<Entitlements> ListUserEntitlements2Async(ListUserEntitlements2Request request, BlazeRpcContext context)
    {
        Console.WriteLine($"[Auth] ListUserEntitlements2: {request.ProjectId}");
        return Task.FromResult(new Entitlements
        {
            mEntitlements = new List<Entitlement>
            {
                new Entitlement
                {
                    EntitlementTag = "FIFA14PC",
                    EntitlementType = EntitlementType.DEFAULT,
                    GroupName = "FIFA14",
                    IsConsumable = false,
                    ProductId = "FIFA14",
                    ProjectId = "FIFA14",
                    Status = EntitlementStatus.ACTIVE,
                    UseCount = 0,
                    Version = 0,
                }
            }
        });
    }

    public override Task<UpdateAccountResponse> UpdateAccountAsync(UpdateAccountRequest request, BlazeRpcContext context)
    {
        Console.WriteLine("[Auth] UpdateAccount");
        return Task.FromResult(new UpdateAccountResponse { PCLoginToken = "fifa14token" });
    }

    public override Task<Entitlements> ListEntitlementsAsync(ListEntitlementsRequest request, BlazeRpcContext context)
    {
        Console.WriteLine("[Auth] ListEntitlements");
        return Task.FromResult(new Entitlements { mEntitlements = new List<Entitlement>() });
    }

    public override Task<AccountInfo> GetAccountAsync(EmptyMessage request, BlazeRpcContext context)
    {
        Console.WriteLine("[Auth] GetAccount");
        return Task.FromResult(new AccountInfo
        {
            AnonymousUser = false,
            Country = "GB",
            Email = Email,
            EmailStatus = EmailStatus.VERIFIED,
            Status = AccountStatus.ACTIVE,
            UserId = UserId,
        });
    }

    public override Task<ListPersonasResponse> ListPersonasAsync(EmptyMessage request, BlazeRpcContext context)
    {
        Console.WriteLine("[Auth] ListPersonas");
        return Task.FromResult(new ListPersonasResponse { List = new List<PersonaDetails> { MakePersona() } });
    }

    public override Task<EmptyMessage> HasEntitlementAsync(HasEntitlementRequest request, BlazeRpcContext context)
    {
        Console.WriteLine($"[Auth] HasEntitlement: {request.EntitlementTag}");
        return Task.FromResult(new EmptyMessage());
    }

    public override Task<EmptyMessage> AcceptTosAsync(AcceptTosRequest request, BlazeRpcContext context)
    {
        Console.WriteLine("[Auth] AcceptTos");
        return Task.FromResult(new EmptyMessage());
    }

    public override Task<GetLegalDocsInfoResponse> GetLegalDocsInfoAsync(GetLegalDocsInfoRequest request, BlazeRpcContext context)
    {
        Console.WriteLine("[Auth] GetLegalDocsInfo");
        return Task.FromResult(new GetLegalDocsInfoResponse());
    }

    public override Task<EmptyMessage> CheckSinglePlayerLoginAsync(EmptyMessage request, BlazeRpcContext context)
    {
        Console.WriteLine("[Auth] CheckSinglePlayerLogin");
        return Task.FromResult(new EmptyMessage());
    }

    private static FullLoginResponse MakeFullLoginResponse(string pcLoginToken) => new()
    {
        SessionInfo = MakeSession(),
        CanAgeUp = false,
        NeedsLegalDoc = false,
        IsOfLegalContactAge = true,
        PCLoginToken = pcLoginToken,
        LegalDocHost = "",
        PrivacyPolicyUri = "",
        TermsOfServiceUri = "",
        TosHost = "",
        TosUri = "",
    };

    private static SessionInfo MakeSession() => new()
    {
        BlazeUserId = UserId,
        IsFirstLogin = false,
        SessionKey = SessionKey,
        LastLoginDateTime = 0,
        Email = Email,
        PersonaDetails = MakePersona(),
        UserId = UserId,
    };

    private static PersonaDetails MakePersona() => new()
    {
        DisplayName = PersonaName,
        LastAuthenticated = 0,
        PersonaId = UserId,
        Status = PersonaStatus.ACTIVE,
    };
}
