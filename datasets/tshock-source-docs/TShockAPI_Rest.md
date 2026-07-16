# 模块: Rest

---

# Rest/Rest.cs

**命名空间**: `Rests`

## 类型定义
- **EscapedParameter**
- **EscapedParameterCollection** : `I`
- **RestRequestArgs**
- **Rest** : `I`

## 方法
### `public EscapedParameter(IParameter parameter)`

### `public EscapedParameterCollection(IParameterCollection collection)`

### `IEnumerator<EscapedParameter> GetEnumerator()`

### `public RestRequestArgs(RestVerbs verbs, EscapedParameterCollection param, IRequest request, SecureRest.TokenData tokenData, IHttpContext context)`

### `public Rest(IPAddress ip, int port)`
Port to listen on

### `virtual void Start()`

### `void Start(IPAddress ip, int port)`
Port to listen on

### `virtual void Stop()`

### `void Register(string path, RestCommandD callback)`
Command callback

### `void Register(RestCommand com)`
to register

### `void RegisterRedirect(string baseRoute, string targetRoute, string upgradeRoute = null, bool parameterized = true)`
Whether the route uses parameterized querying or not.

### `void AddCommand(RestCommand com)`
to add

### `void DegradeBucket()`

### `virtual void OnRequest(object sender, RequestEventArgs e)`
RequestEventArgs received

### `virtual object ProcessRequest(object sender, RequestEventArgs e)`
A  describing the state of the request

### `virtual object ExecuteCommand(RestCommand cmd, RestVerbs verbs, IParameterCollection parms, IRequest request, IHttpContext context)`

### `virtual string BuildRequestUri(RestCommand cmd, RestVerbs verbs, IParameterCollection parms, bool includeToken = true)`

### `void Dispose()`

### `virtual void Dispose(bool disposing)`

### `IEnumerator IEnumerable.GetEnumerator()`

---

# Rest/RestCommand.cs

**命名空间**: `Rests`

## 类型定义
- **RestCommand**
- **SecureRestCommand** : `R`

## 方法
### `public RestCommand(string name, string uritemplate, RestCommandD callback)`
Rest Command callback

### `virtual object Execute(RestVerbs verbs, IParameterCollection parameters, IRequest request, IHttpContext context)`

### `object Execute(RestVerbs verbs, IParameterCollection parameters, SecureRest.TokenData tokenData, IRequest request, IHttpContext context)`

---

# Rest/RestManager.cs

**命名空间**: `TShockAPI`

## 类型定义
- **Permission** : `A`
- **RouteAttribute** : `A`
- **ParameterAttribute** : `A`
- **Noun** : `P`
- **Verb** : `P`
- **Token** : `N`
- **RestManager**

## 方法
### `public Permission(string name)`
Permission required

### `public RouteAttribute(string route)`
Route used to call the API

### `public ParameterAttribute(string name, bool req, string desc, Type type)`

### `public RestManager(Rest rest)`

### `void RegisterRestfulCommands()`

### `[Token]
		private object ServerCommandV3(RestRequestArgs args)`

### `[Token]
		private object ServerOff(RestRequestArgs args)`

### `[Token]
		private object ServerReload(RestRequestArgs args)`

### `[Token]
		private object ServerBroadcast(RestRequestArgs args)`

### `[Token]
		private object ServerMotd(RestRequestArgs args)`

### `[Token]
		private object ServerRules(RestRequestArgs args)`

### `[Token]
		private object ServerStatusV2(RestRequestArgs args)`

### `[Token]
		private object ServerTokenTest(RestRequestArgs args)`

### `[Token]
		private object UserActiveListV2(RestRequestArgs args)`

### `[Token]
		private object UserListV2(RestRequestArgs args)`

### `[Token]
		private object UserCreateV2(RestRequestArgs args)`

### `[Token]
		private object UserUpdateV2(RestRequestArgs args)`

### `[Token]
		private object UserDestroyV2(RestRequestArgs args)`

### `[Token]
		private object UserInfoV2(RestRequestArgs args)`

### `[Token]
		private object BanCreateV3(RestRequestArgs args)`

### `[Token]
		private object BanDestroyV3(RestRequestArgs args)`

### `[Token]
		private object BanInfoV3(RestRequestArgs args)`

### `[Token]
		private object BanListV3(RestRequestArgs args)`

### `[Token]
		private object WorldChangeSaveSettings(RestRequestArgs args)`

### `[Token]
		private object WorldChangeSaveSettingsV3(RestRequestArgs args)`

### `[Token]
		private object WorldSave(RestRequestArgs args)`

### `[Token]
		private object WorldButcher(RestRequestArgs args)`

### `[Token]
		private object WorldRead(RestRequestArgs args)`

### `[Token]
		private object WorldMeteor(RestRequestArgs args)`

### `[Token]
		private object WorldBloodmoon(RestRequestArgs args)`

### `[Token]
		private object WorldBloodmoonV3(RestRequestArgs args)`

### `[Token]
		private object PlayerUnMute(RestRequestArgs args)`

### `[Token]
		private object PlayerMute(RestRequestArgs args)`

### `[Token]
		private object PlayerList(RestRequestArgs args)`

### `[Token]
		private object PlayerListV2(RestRequestArgs args)`

### `[Token]
		private object PlayerReadV3(RestRequestArgs args)`

### `[Token]
		private object PlayerReadV4(RestRequestArgs args)`

### `[Token]
		private object PlayerKickV2(RestRequestArgs args)`

### `[Token]
		private object PlayerKill(RestRequestArgs args)`

### `[Token]
		private object GroupList(RestRequestArgs args)`

### `[Token]
		private object GroupInfo(RestRequestArgs args)`

### `[Token]
		private object GroupDestroy(RestRequestArgs args)`

### `[Token]
		private object GroupCreate(RestRequestArgs args)`

### `[Token]
		private object GroupUpdate(RestRequestArgs args)`

### `figure out how to localise the route descriptions
		public static void DumpDescriptions()`

### `RestObject RestError(string message, string status = "400")`

### `RestObject RestResponse(string message, string status = "200")`

### `RestObject RestMissingParam(string var)`

### `RestObject RestMissingParam(params string[] vars)`

### `RestObject RestInvalidParam(string var)`

### `bool GetBool(string val, bool def)`

### `object PlayerFind(EscapedParameterCollection parameters)`

### `object UserFind(EscapedParameterCollection parameters)`

### `object GroupFind(EscapedParameterCollection parameters)`

### `Dictionary<string, object> PlayerFilter(TSPlayer tsPlayer, EscapedParameterCollection parameters, bool viewips = false)`

### `object PlayerSetMute(EscapedParameterCollection parameters, bool mute)`

### ` Rest.Register(new SecureRestCommand("/v2/users/create", UserCreateV2, RestPermissions.restmanageusers)`

### ` Rest.Register(new SecureRestCommand("/v2/users/update", UserUpdateV2, RestPermissions.restmanageusers)`

---

# Rest/RestObject.cs

**命名空间**: `Rests`

## 类型定义
- **RestObject** : `D`

## 方法
### `public RestObject()`

### `public RestObject(string status = "200")`

---

# Rest/SecureRest.cs

**命名空间**: `Rests`

## 类型定义
- **SecureRest** : `R`
- **TokenData**

## 方法
### `void AddTokenToBucket(string ip)`

### `object DestroyToken(RestRequestArgs args)`

### `object DestroyAllTokens(RestRequestArgs args)`

### `object NewTokenV2(RestRequestArgs args)`

### `RestObject NewTokenInternal(string username, string password, IHttpContext context)`

### `Tokens over limit, increment by one and reject request
					return new RestObject("403")`

### `override object ExecuteCommand(RestCommand cmd, RestVerbs verbs, IParameterCollection parms, IRequest request, IHttpContext context)`
