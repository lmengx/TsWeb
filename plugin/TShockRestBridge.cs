using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using HttpServer;
using Newtonsoft.Json;
using Rests;
using TShockAPI;

namespace TShockData
{
    /// <summary>
    /// 透传桥：把 WebRestServer 收到的普通 REST 请求，交给 TShock 原 REST 处理（Rest.ProcessRequest）。
    /// 通过 DispatchProxy 动态实现 HttpServer.dll 的接口（IRequest/IResponse/IHttpContext/IParameterCollection/IParameter），
    /// 不依赖接口完整成员签名；路由匹配 / token 鉴权 / 限流 / 404 全部复用 TShock 原逻辑。
    /// </summary>
    public static class TShockRestBridge
    {
        private static MethodInfo? _processRequest;
        private static ConstructorInfo? _requestEventArgsCtor;
        private static int _ctxIdx = -1, _reqIdx = -1, _respIdx = -1; // 构造参数位置
        private static bool _ready;

        private static void EnsureReady()
        {
            if (_ready) return;
            lock (typeof(TShockRestBridge))
            {
                if (_ready) return;
                try
                {
                    _processRequest = typeof(Rests.Rest).GetMethod("ProcessRequest",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                    // RequestEventArgs 有两个来源：TShockAPI.Rests.RequestEventArgs（ProcessRequest 的参数类型）
                    // 与 HttpServer.RequestEventArgs。TShock 用的是前者，且可能是 internal（不能直接 typeof），
                    // 因此用字符串反射获取；找不到再退回到 HttpServer 的类型。
                    _requestEventArgsCtor = null;
                    _ctxIdx = _reqIdx = _respIdx = -1;
                    var reaType = typeof(Rests.Rest).Assembly.GetType("Rests.RequestEventArgs")
                                  ?? typeof(RequestEventArgs);
                    if (reaType != null)
                    {
                        foreach (var ctor in reaType.GetConstructors(
                                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            var ps = ctor.GetParameters();
                            int ci = -1, ri = -1, spi = -1;
                            if (ps.Length == 3)
                            {
                                for (var i = 0; i < 3; i++)
                                {
                                    var pt = ps[i].ParameterType;
                                    if (pt == typeof(IHttpContext)) ci = i;
                                    else if (pt == typeof(IRequest)) ri = i;
                                    else if (pt == typeof(IResponse)) spi = i;
                                }
                            }
                            if (ci >= 0 && ri >= 0 && spi >= 0)
                            {
                                _requestEventArgsCtor = ctor;
                                _ctxIdx = ci; _reqIdx = ri; _respIdx = spi;
                                break;
                            }
                        }
                    }

                    _ready = _processRequest != null && _requestEventArgsCtor != null;
                    if (!_ready)
                    {
                        var sb = new StringBuilder("[TSWeb] REST 桥初始化失败: ");
                        sb.Append(_processRequest == null ? "ProcessRequest 未找到; " : "");
                        sb.Append($"RequestEventArgs 类型=({reaType?.FullName ?? "null"}) 无可用构造; ");
                        if (reaType != null)
                            foreach (var ctor in reaType.GetConstructors(
                                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                            {
                                sb.Append("(");
                                sb.Append(string.Join(",", Array.ConvertAll(ctor.GetParameters(),
                                    p => p.ParameterType.FullName)));
                                sb.Append(") ");
                            }
                        TShock.Log.ConsoleError(sb.ToString());
                    }
                }
                catch (Exception ex)
                {
                    _ready = false;
                    TShock.Log.ConsoleError($"[TSWeb] REST 桥初始化异常: {ex}");
                }
            }
        }

        /// <summary>透传结果</summary>
        public sealed class RestBridgeResult
        {
            public string Body = "";
            public HttpStatusCode Status = HttpStatusCode.OK;
            public string ContentType = "application/json; charset=utf-8";
        }

        /// <summary>
        /// 透传一个请求到 TShock 原 REST 处理。
        /// </summary>
        /// <param name="path">URL 路径（不含 query，未 TrimEnd('/')）</param>
        /// <param name="rawQuery">原始 query 字符串（未解码；无则为空串）</param>
        /// <param name="formParams">POST 表单参数（原始值，未解码；无则为 null）</param>
        public static RestBridgeResult Process(string path, string rawQuery, Dictionary<string, string>? formParams,
            IPEndPoint? remoteEndPoint = null, string? method = null)
        {
            EnsureReady();
            if (!_ready)
            {
                return new RestBridgeResult
                {
                    Body = JsonConvert.SerializeObject(new { status = "500", error = "TShock REST 桥初始化失败" }),
                    Status = HttpStatusCode.InternalServerError
                };
            }

            // 参数合并：query 与 form（query 优先，与 HttpServer 库行为一致）
            var bag = new QueryBag();
            if (formParams != null)
                foreach (var kv in formParams) bag[kv.Key] = kv.Value;
            if (!string.IsNullOrEmpty(rawQuery))
                foreach (var pair in rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var idx = pair.IndexOf('=');
                    if (idx < 0) bag[Uri.UnescapeDataString(pair)] = "";
                    else bag[Uri.UnescapeDataString(pair.Substring(0, idx))] = pair.Substring(idx + 1);
                }

            // URL（AbsolutePath 不含 query）
            var cleanPath = string.IsNullOrEmpty(path) ? "/" : path;
            var uri = new Uri("http://localhost" + cleanPath);

            var reqData = new RequestData { Uri = uri, Parameters = bag, Method = method };
            var requestProxy = Create<IRequest>(reqData);
            var responseProxy = Create<IResponse>(null);
            var ctxData = new ContextData { RequestProxy = requestProxy, ResponseProxy = responseProxy, RemoteEndPoint = remoteEndPoint };
            var contextProxy = Create<IHttpContext>(ctxData);

            try
            {
                var ctorArgs = new object[3];
                ctorArgs[_ctxIdx] = contextProxy;
                ctorArgs[_reqIdx] = requestProxy;
                ctorArgs[_respIdx] = responseProxy;
                var args = _requestEventArgsCtor!.Invoke(ctorArgs);
                var result = _processRequest!.Invoke(TShock.RestApi, new[] { null, args });

                // 复刻 Rest.OnRequest 的序列化逻辑
                var json = JsonConvert.SerializeObject(result, Formatting.Indented);
                if (bag.TryGetValue("jsonp", out var jp) && !string.IsNullOrWhiteSpace(jp))
                    json = $"{jp}({json});";

                var status = HttpStatusCode.OK;
                if (result is RestObject ro && Enum.TryParse<HttpStatusCode>(ro.Status, out var sc))
                    status = sc;

                return new RestBridgeResult { Body = json, Status = status };
            }
            catch (Exception ex)
            {
                return new RestBridgeResult
                {
                    Body = JsonConvert.SerializeObject(new { status = "500", error = "Internal server error.", errormsg = ex.Message }),
                    Status = HttpStatusCode.InternalServerError
                };
            }
        }

        // ═══════════════ DispatchProxy 工厂 ═══════════════

        private static T Create<T>(object? payload) where T : class
        {
            var proxy = DispatchProxy.Create<T, RestDispatchProxy>();
            var dp = (RestDispatchProxy)(object)proxy;
            dp.Payload = payload;
            return proxy;
        }

        internal class RestDispatchProxy : DispatchProxy
        {
            public object? Payload;
            protected override object? Invoke(MethodInfo targetMethod, object?[]? args)
                => TShockRestBridge.Dispatch(this, targetMethod, args);
        }

        private static object? Dispatch(RestDispatchProxy proxy, MethodInfo m, object?[]? args)
        {
            var name = m.Name;
            var iface = m.DeclaringType;

            // ── IParameter ──
            if (typeof(IParameter).IsAssignableTo(iface))
            {
                var kv = (KeyValuePair<string, string>)proxy.Payload!;
                if (name == "get_Name") return kv.Key;
                if (name == "get_Value") return kv.Value;
                return null;
            }

            // ── IParameterCollection ──
            if (typeof(IParameterCollection).IsAssignableTo(iface))
            {
                var bag = (QueryBag)proxy.Payload!;
                if (name == "get_Item")
                {
                    // 索引器返回 string（TShock SecureRest 依赖 parms["token"] 为 string）
                    var key = (string)args![0]!;
                    return bag.TryGetValue(key, out var v) ? v : null;
                }
                if (name == "GetEnumerator") return new BagEnumerator(bag);
                if (name == "get_Count") return bag.Count;
                if (name == "get_Names") return bag.Keys;
                if (name == "Contains") return bag.ContainsKey((string)args![0]!);
                if (name == "Add") { bag[(string)args![0]!] = (string)args![1]!; return null; }
                return null;
            }

            // ── IRequest ──
            if (iface == typeof(IRequest))
            {
                var data = (RequestData)proxy.Payload!;
                if (name == "get_Uri") return data.Uri;
                if (name == "get_Method") return data.Method;
                if (name == "get_Parameters") return data.ParametersProxy ??= MakeParameterCollection(data.Parameters);
                if (name == "get_Form") return data.FormProxy ??= MakeParameterCollection(data.Form ?? new QueryBag());
                if (name == "get_Method") return null;
                return null;
            }

            // ── IResponse（透传时不会被写入，占位） ──
            if (iface == typeof(IResponse))
            {
                if (name == "get_Sent") return false;
                if (name == "get_Status") return HttpStatusCode.OK;
                if (name == "set_Status") return null;
                return null;
            }

            // ── IHttpContext ──
            if (iface == typeof(IHttpContext))
            {
                var data = (ContextData)proxy.Payload!;
                if (name == "get_Request") return data.RequestProxy;
                if (name == "get_Response") return data.ResponseProxy;
                if (name == "get_RemoteEndPoint") return data.RemoteEndPoint;
                return null;
            }

            return null;
        }

        private static IParameter MakeParameter(string name, string value)
        {
            var proxy = Create<IParameter>(new KeyValuePair<string, string>(name, value));
            return proxy;
        }

        private static IParameterCollection MakeParameterCollection(QueryBag bag)
        {
            return Create<IParameterCollection>(bag);
        }

        // ═══════════════ 数据容器 ═══════════════

        /// <summary>参数集合（值存原始编码，EscapedParameterCollection 负责 unescape）</summary>
        internal sealed class QueryBag : Dictionary<string, string>
        {
            public QueryBag() : base(StringComparer.OrdinalIgnoreCase) { }
        }

        internal sealed class RequestData
        {
            public Uri? Uri;
            public string? Method;
            public QueryBag? Parameters;
            public QueryBag? Form;
            public IParameterCollection? ParametersProxy;
            public IParameterCollection? FormProxy;
        }

        internal sealed class ContextData
        {
            public IRequest? RequestProxy;
            public IResponse? ResponseProxy;
            public IPEndPoint? RemoteEndPoint;
        }

        /// <summary>非泛型 IEnumerator（HttpServer 库 IParameterCollection 继承 IEnumerable）</summary>
        internal sealed class BagEnumerator : IEnumerator
        {
            private readonly QueryBag _bag;
            private readonly IEnumerator<KeyValuePair<string, string>> _inner;
            public BagEnumerator(QueryBag bag) { _bag = bag; _inner = bag.GetEnumerator(); }
            public bool MoveNext() => _inner.MoveNext();
            public void Reset() { /* Dictionary.Enumerator.Reset 不支持，空实现 */ }
            public object Current => MakeParameter(_inner.Current.Key, _inner.Current.Value);
        }
    }
}
