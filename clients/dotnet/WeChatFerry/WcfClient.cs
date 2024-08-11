using Google.Protobuf;
using Grpc.Core;
using static nng;

namespace WeChatFerry;

public class WcfClient(string host = "127.0.0.1", int port = 10086) : WcfProtobuf.WcfProtobufClient(new WcfChannel(host, port))
{
    public delegate void MessageReceivedHandler(WxMsg msg);

    public event MessageReceivedHandler? MessageReceived
    {
        add
        {
            lock (this)
            {
                if (messageReceiveTask == null)
                {
                    if (Call(new Request { Func = Functions.FuncEnableRecvTxt }).Status != 0) 
                        throw new ApplicationException("failed to enable message receive");

                    messageReceiveCts = new CancellationTokenSource();
                    messageReceiveTask = ReceiveMessageWork(messageReceiveCts.Token);
                }
                
                messageReceived += value;
            }
        }
        remove
        {
            lock (this)
            {
                messageReceived -= value;
                if (messageReceived != null) return;
                
                if (Call(new Request { Func = Functions.FuncDisableRecvTxt }).Status != 0) 
                    throw new ApplicationException("failed to disable message receive");
                
                messageReceiveCts?.Cancel();
                messageReceiveTask = null;
            }
        }
    }

    private MessageReceivedHandler? messageReceived;
    private Task? messageReceiveTask;
    private CancellationTokenSource? messageReceiveCts;

    private readonly string msgSocketAddress = $"tcp://{host}:{port + 1}";

    /// <summary>
    /// 是否已登录
    /// </summary>
    public bool IsLogin => Call(new Request { Func = Functions.FuncIsLogin }).Status == 1;

    /// <summary>
    /// 自己的微信id
    /// </summary>
    public string MyWxid => Call(new Request { Func = Functions.FuncGetSelfWxid }).Str;

    /// <summary>
    /// 获取消息类型列表
    /// </summary>
    public IReadOnlyDictionary<int, string> MsgTypes => Call(new Request { Func = Functions.FuncGetMsgTypes }).Types_.Types_;

    /// <summary>
    /// 获取通讯录
    /// </summary>
    public IReadOnlyList<RpcContact> Contacts => Call(new Request { Func = Functions.FuncGetContacts }).Contacts.Contacts;
    
    public IReadOnlyList<string> DbNames => Call(new Request { Func = Functions.FuncGetDbNames }).Dbs.Names;

    public IReadOnlyList<DbTable> GetDbTables(string dbName) => Call(new Request { Func = Functions.FuncGetDbTables, Str = dbName }).Tables.Tables;

    public IReadOnlyList<DbRow> ExecDbQuery(string dbName, string sql) => Call(new Request
    {
        Func = Functions.FuncExecDbQuery, Query = new DbQuery
        {
            Db = dbName, Sql = sql
        }
    }).Rows.Rows;

    public enum AcceptFriendMethod
    {
        /// <summary>
        /// 名片
        /// </summary>
        Card = 17,
        /// <summary>
        /// 扫码
        /// </summary>
        ScanQrCode = 30
    }

    public int AcceptFriend(string encryptUsername, string ticket, AcceptFriendMethod method) => Call(new Request
    {
        Func = Functions.FuncAcceptFriend, 
        V = new Verification
        {
            V3 = encryptUsername,
            V4 = ticket,
            Scene = (int)method,
        }
    }).Status;

    public int SendTextMessage(string message, string receiver, params string[] ats) => Call(new Request
    {
        Func = Functions.FuncSendTxt,
        Txt = new TextMsg
        {
            Msg = message,
            Receiver = receiver,
            Aters = string.Join(',', ats)
        }
    }).Status;

    public int SendFileMessage(string path, string receiver) => Call(new Request
    {
        Func = Functions.FuncSendImg,
        File = new PathMsg
        {
            Path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)),
            Receiver = receiver
        }
    }).Status;

    public int SendXmlMessage(string path, string content, string receiver, int type) => Call(new Request
    {
        Func = Functions.FuncSendXml,
        Xml = new XmlMsg
        {
            Path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)),
            Content = content,
            Receiver = receiver,
            Type = type
        }
    }).Status;

    /// <summary>
    /// 发送卡片消息
    /// </summary>
    /// <param name="name">左下显示的名字</param>
    /// <param name="account">填公众号 id 可以显示对应的头像（gh_ 开头的）</param>
    /// <param name="title">标题，最多两行</param>
    /// <param name="digest">摘要，三行</param>
    /// <param name="url">点击后跳转的链接</param>
    /// <param name="thumbUrl">缩略图的链接</param>
    /// <param name="receiver">接收人, wxid 或者 roomId</param>
    /// <returns></returns>
    /// <remarks>
    /// {
    ///     "name": "关注公众号: 一条爱睡觉的咸鱼",
    ///     "account": "gh_0c617dab0f5f",
    ///     "title": "测试",
    ///     "digest": "测试",
    ///     "url": "https://apifox.com/apidoc/shared-edbfcebc-6263-4e87-9813-54520c1b3c19",
    ///     "thumb_url": "https://wx.qlogo.cn/mmopen/r48cSSlr7jgFutEJFpmolCux6WWZsm92KLTOmWITDvqPVIO5kLpTblfqsxuGzaZvGkgHsBOohkWuZlZuF48hRVEIcjRu1wVF/64",
    ///     "receiver": "39139856094@chatroom"
    /// }
    /// </remarks>
    public int SendCardMessage(string name, string account, string title, string digest, string url, string thumbUrl, string receiver) => Call(
        new Request
        {
            Func = Functions.FuncSendRichTxt,
            Rt = new RichText
            {
                Name = name,
                Account = account,
                Title = title,
                Digest = digest,
                Url = url,
                Thumburl = thumbUrl,
                Receiver = receiver
            }
        }).Status;

    public int SendEmojiMessage(string path, string receiver) => Call(new Request
    {
        Func = Functions.FuncSendEmotion,
        File = new PathMsg
        {
            Path = path,
            Receiver = receiver
        }
    }).Status;

    /// <summary>
    /// 发送拍一拍消息
    /// </summary>
    /// <param name="roomId"></param>
    /// <param name="wxid"></param>
    /// <returns></returns>
    public int SendPatMessage(string roomId, string wxid) => Call(new Request
    {
        Func = Functions.FuncSendPatMsg,
        Pm = new PatMsg
        {
            Roomid = roomId,
            Wxid = wxid
        }
    }).Status;

    private Task ReceiveMessageWork(CancellationToken cancellationToken)
    {
        return Task.Factory.StartNew(() =>
            {
                using var msgSocket = new NngPairSocket(msgSocketAddress);
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (msgSocket.TryReceive() is not {} buffer) continue;
                    messageReceived?.Invoke(Response.Parser.ParseFrom(buffer).Wxmsg);
                }
            },
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Current);
    }

    private class NngPairSocket : IDisposable
    {
        private readonly nng_socket socket;

        public NngPairSocket(string address)
        {
            nng_assert(nng_pair1_open(ref socket));
            nng_assert(nng_socket_set_ms(socket, "recv-timeout", new nng_duration(5000)));
            nng_assert(nng_socket_set_ms(socket, "send-timeout", new nng_duration(5000)));
        
            nng_dialer dialer = default;
            nng_assert(nng_dial(socket, address, ref dialer, 0));
        }

        public void Send(ReadOnlySpan<byte> data)
        {
            nng_assert(nng_send(socket, data));
        }

        private unsafe int NngReceive(out IntPtr buffer, out size_t size)
        {
            IntPtr pBuffer = default;
            size_t sizeT = default;
            var result = nng_recv(socket, new IntPtr(&pBuffer), ref sizeT, NNG_FLAG_ALLOC);
            buffer = result == 0 && pBuffer != IntPtr.Zero ? pBuffer : default;
            size = sizeT;
            return result;
        }

        public unsafe byte[] Receive()
        {
            var result = NngReceive(out var buffer, out var size);
            try
            {
                nng_assert(result);
                return buffer == IntPtr.Zero ? [] : new Span<byte>(buffer.ToPointer(), (int)(long)size).ToArray();
            }
            finally
            {
                if (buffer != IntPtr.Zero) nng_free(buffer, size);
            }
        }

        public unsafe byte[]? TryReceive()
        {
            var result = NngReceive(out var buffer, out var size);
            try
            {
                return result != 0 || buffer == IntPtr.Zero ? null : new Span<byte>(buffer.ToPointer(), (int)(long)size).ToArray();
            }
            finally
            {
                if (buffer != IntPtr.Zero) nng_free(buffer, size);
            }
        }

        public void Dispose()
        {
            nng_assert(nng_close(socket));
        }
    }

    /// <summary>
    /// Nanomsg based
    /// </summary>
    private class WcfChannel(string host, int port) : ChannelBase(host)
    {
        private readonly NngPairSocket cmdSocket = new($"tcp://{host}:{port}");

        public override CallInvoker CreateCallInvoker() => new WcfCallInvoker(cmdSocket);

        protected override Task ShutdownAsyncCore()
        {
            cmdSocket.Dispose();
            return base.ShutdownAsyncCore();
        }

        private class WcfCallInvoker(NngPairSocket socket) : CallInvoker
        {
            private readonly Marshaller<Request> requestMarshaller = Marshallers.Create(MessageExtensions.ToByteArray, Request.Parser.ParseFrom);
            private readonly Marshaller<Response> responseMarshaller = Marshallers.Create(MessageExtensions.ToByteArray, Response.Parser.ParseFrom);
            
            public override TResponse BlockingUnaryCall<TRequest, TResponse>(
                Method<TRequest, TResponse> method,
                string? host,
                CallOptions options,
                TRequest request)
            {
                if (request is not Request req) throw new ArgumentException("must be type Request", nameof(request));
                
                socket.Send(requestMarshaller.Serializer(req));
                var buffer = socket.Receive();
                if (buffer.Length == 0) throw new ApplicationException("empty response");
                if (responseMarshaller.Deserializer(buffer) is not TResponse res) throw new ApplicationException("response type mismatch");
                return res;
            }

            public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
                Method<TRequest, TResponse> method,
                string? host,
                CallOptions options,
                TRequest request)
            {
                throw new NotSupportedException();
            }

            public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
                Method<TRequest, TResponse> method,
                string? host,
                CallOptions options,
                TRequest request)
            {
                throw new NotSupportedException();
            }

            public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
                Method<TRequest, TResponse> method,
                string? host,
                CallOptions options)
            {
                throw new NotSupportedException();
            }

            public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
                Method<TRequest, TResponse> method,
                string? host,
                CallOptions options)
            {
                throw new NotSupportedException();
            }
        }
    }
}

// 0, 朋友圈消息
// 1, 文字
// 3, 图片
// 34, 语音
// 37, 好友确认
// 40, POSSIBLEFRIEND_MSG
// 42, 名片
// 43, 视频
// 47, 石头剪刀布 | 表情图片
// 48, 位置
// 49, 共享实时位置、文件、转账、链接
// 50, VOIPMSG
// 51, 微信初始化
// 52, VOIPNOTIFY
// 53, VOIPINVITE
// 62, 小视频
// 66, 微信红包
// 9999, SYSNOTICE
// 10000, 红包、系统消息
// 10002, 撤回消息
// 1048625, 搜狗表情
// 16777265, 链接
// 436207665, 微信红包
// 536936497, 红包封面
// 754974769, 视频号视频
// 771751985, 视频号名片
// 822083633, 引用消息
// 922746929, 拍一拍
// 973078577, 视频号直播
// 974127153, 商品链接
// 975175729, 视频号直播
// 1040187441, 音乐链接
// 1090519089, 文件
public enum WxMsgType : uint
{
    /// <summary>
    /// 朋友圈消息
    /// </summary>
    Moments = 0,
    /// <summary>
    /// 文字
    /// </summary>
    Text = 1,
    /// <summary>
    /// 图片
    /// </summary>
    Image = 3,
    /// <summary>
    /// 语音
    /// </summary>
    Voice = 34,
    /// <summary>
    /// 好友确认
    /// </summary>
    FriendConfirmation = 37,
    /// <summary>
    /// POSSIBLEFRIEND_MSG
    /// </summary>
    PossibleFriend = 40,
    /// <summary>
    /// 名片
    /// </summary>
    Card = 42,
    /// <summary>
    /// 视频
    /// </summary>
    Video = 43,
    /// <summary>
    /// 石头剪刀布 | 表情图片
    /// </summary>
    EmojiImage = 47,
    /// <summary>
    /// 位置
    /// </summary>
    Location = 48,
    /// <summary>
    /// 共享实时位置、文件、转账、链接
    /// </summary>
    Share = 49,
    /// <summary>
    /// VOIPMSG
    /// </summary>
    VoipMsg = 50,
    /// <summary>
    /// 微信初始化
    /// </summary>
    WxInit = 51,
    /// <summary>
    /// VOIPNOTIFY
    /// </summary>
    VoipNotify = 52,
    /// <summary>
    /// VOIPINVITE
    /// </summary>
    VoipInvite = 53,
    /// <summary>
    /// 小视频
    /// </summary>
    SmallVideo = 62,
    /// <summary>
    /// 微信红包
    /// </summary>
    RedEnvelope = 66,
    /// <summary>
    /// SYSNOTICE
    /// </summary>
    SysNotice = 9999,
    /// <summary>
    /// 红包、系统消息
    /// </summary>
    RedEnvelopeSystem = 10000,
    /// <summary>
    /// 撤回消息
    /// </summary>
    Revoke = 10002,
    /// <summary>
    /// 搜狗表情
    /// </summary>
    SogouEmoji = 1048625,
    /// <summary>
    /// 链接
    /// </summary>
    Link = 16777265,
    /// <summary>
    /// 微信红包
    /// </summary>
    RedEnvelope2 = 436207665,
    /// <summary>
    /// 红包封面
    /// </summary>
    RedEnvelopeCover = 536936497,
    /// <summary>
    /// 视频号视频
    /// </summary>
    VideoAccountVideo = 754974769,
    /// <summary>
    /// 视频号名片
    /// </summary>
    VideoAccountCard = 771751985,
    /// <summary>
    /// 引用消息
    /// </summary>
    Quote = 822083633,
    /// <summary>
    /// 拍一拍
    /// </summary>
    Pat = 922746929,
    /// <summary>
    /// 视频号直播
    /// </summary>
    VideoAccountLive = 973078577,
    /// <summary>
    /// 商品链接
    /// </summary>
    ProductLink = 974127153,
    /// <summary>
    /// 视频号直播
    /// </summary>
    VideoAccountLive2 = 975175729,
    /// <summary>
    /// 音乐链接
    /// </summary>
    MusicLink = 1040187441,
    /// <summary>
    /// 文件
    /// </summary>
    File = 1090519089
}