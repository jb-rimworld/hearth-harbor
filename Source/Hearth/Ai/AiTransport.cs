// SPDX-License-Identifier: MIT
using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Hearth.Ai
{
    public sealed class AiHttpResult
    {
        /// <summary>HTTP status, 0 when none arrived.</summary>
        public int Status;
        public string Body;
        /// <summary>No HTTP answer at all: connection refused, DNS, TLS, timeout of the request itself, aborted.</summary>
        public bool TransportFailed;
        public string Error;
    }

    /// <summary>A request in flight. Polled from the main thread (AiService.Pump); never blocks.</summary>
    public interface IAiHandle
    {
        bool IsDone { get; }
        AiHttpResult Result { get; }
        void Abort();
    }

    /// <summary>
    /// The only door to the network. Post/Get return at once and the answer is read later by polling <see cref="IAiHandle.IsDone"/> from the main thread,
    /// so a slow or dead relay can never stall a frame. Tests replace the transport by a fake (no network).
    /// </summary>
    public interface IAiTransport
    {
        IAiHandle Post(string url, string json, string clientVersion, int timeoutSeconds);
        IAiHandle Get(string url, string clientVersion, int timeoutSeconds);
    }

    /// <summary>UnityWebRequest on the main thread: SendWebRequest is asynchronous, the result is picked up by polling isDone. No certificate handler is installed (the platform's trust store decides).</summary>
    public sealed class UnityAiTransport : IAiTransport
    {
        public IAiHandle Post(string url, string json, string clientVersion, int timeoutSeconds)
        {
            var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            byte[] body = new UTF8Encoding(false).GetBytes(json ?? "");
            req.uploadHandler = new UploadHandlerRaw(body) { contentType = "application/json" };
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            return Start(req, clientVersion, timeoutSeconds);
        }

        public IAiHandle Get(string url, string clientVersion, int timeoutSeconds)
        {
            var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET) { downloadHandler = new DownloadHandlerBuffer() };
            return Start(req, clientVersion, timeoutSeconds);
        }

        static IAiHandle Start(UnityWebRequest req, string clientVersion, int timeoutSeconds)
        {
            req.timeout = Math.Max(5, timeoutSeconds);
            req.redirectLimit = 0;
            req.SetRequestHeader("Accept", "application/json");
            req.SetRequestHeader("X-HH-Client", clientVersion);
            req.SendWebRequest();
            return new Handle(req);
        }

        sealed class Handle : IAiHandle
        {
            UnityWebRequest req; AiHttpResult result;
            public Handle(UnityWebRequest r) { req = r; }
            public bool IsDone => result != null || req == null || req.isDone;
            public AiHttpResult Result
            {
                get
                {
                    if (result != null) return result;
                    if (!IsDone) return null;
                    var r = new AiHttpResult();
                    try
                    {
                        r.Status = (int)req.responseCode;
                        r.Body = req.downloadHandler != null ? req.downloadHandler.text : null;
                        // ProtocolError still carries a body (the relay's JSON error); only a missing connection is a transport failure
#if RW15
                        r.TransportFailed = req.isNetworkError || r.Status == 0;   // Unity 2019: no Result enum
#else
                        r.TransportFailed = req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.DataProcessingError || r.Status == 0;
#endif
                        r.Error = r.TransportFailed ? req.error : null;
                    }
                    catch (Exception e) { r.TransportFailed = true; r.Error = e.GetType().Name; }
                    finally { try { req.Dispose(); } catch (Exception) { } req = null; }
                    return result = r;
                }
            }
            public void Abort()
            {
                if (result != null || req == null) return;
                try { req.Abort(); } catch (Exception) { }
                try { req.Dispose(); } catch (Exception) { }
                req = null;
                result = new AiHttpResult { TransportFailed = true, Error = "aborted" };
            }
        }
    }
}
