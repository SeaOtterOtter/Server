using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Threading;


public class ServerSession
{
    public int SessionId { get; set; }


    Socket _socket;
    int _disconnected = 0;
    CancellationToken _cToken;
    RecvBuffer _recvBuffer = new RecvBuffer(65535);

    object _lock = new object();
    Queue<ArraySegment<byte>> _sendQueue = new Queue<ArraySegment<byte>>();
    List<ArraySegment<byte>> _pendingList = new List<ArraySegment<byte>>();
    public SocketAsyncEventArgs _sendArgs = new SocketAsyncEventArgs();
    public SocketAsyncEventArgs _recvArgs = new SocketAsyncEventArgs();

    public void OnConnected(EndPoint endPoint)
    {

        //GameManager.PrintLog("connected");


      
        
        
        
        //room push
    }
    public int OnRecv(ArraySegment<byte> buffer)
    {
        int processLen = 0;
        int packetCount = 0;
        //GameManager.PrintLog($"OnRecv ");
        while (true)
        {
           

            if(_cToken.IsCancellationRequested)
            {
                GameManager.PrintLog("OnRecv is Cancled");
                break;
            }


            // �ּ��� ��Ŷ�� ������� ���� �� �ִ���
            if (buffer.Count < ClientPacketManager.sizeOfPacketSize)
                break;

            // ��Ŷ�� ����ü�� �����ߴ��� Ȯ��
            ushort dataSize = BitConverter.ToUInt16(buffer.Array, buffer.Offset);

            if (buffer.Count < dataSize)
                break;


            

            // ������� ������ ��Ŷ ���� ����
            OnRecvPacket(new ArraySegment<byte>(buffer.Array, buffer.Offset, dataSize));
            packetCount++;

            processLen += dataSize;
            buffer = new ArraySegment<byte>(buffer.Array, buffer.Offset + dataSize, buffer.Count - dataSize);
        }

      

        return processLen;
    }

    //��Ŷ ����
    public void OnRecvPacket(ArraySegment<byte> buffer)
    {
        ClientPacketManager.Instance.OnRecvPacket(this, buffer);

    }
    public void OnSend(int numOfBytes)
    {
        //GameManager.PrintLog(numOfBytes + " ����");
    }
    public void OnDisconnected(EndPoint endPoint)
    {

    }

    void Clear()
    {
        lock (_lock)
        {
            _sendQueue.Clear();
            _pendingList.Clear();
        }
    }

    public void Start(Socket socket , CancellationToken cToken)
    {
        //GameManager.PrintLog("start!!");
        _socket = socket;
        _cToken = cToken;

        _recvArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnRecvCompleted);
        _sendArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnSendCompleted);



        RegisterRecv();
    }

    public void Send(List<ArraySegment<byte>> sendBuffList)
    {
        if (sendBuffList.Count == 0)
            return;

        lock (_lock)
        {
            foreach (ArraySegment<byte> sendBuff in sendBuffList)
                _sendQueue.Enqueue(sendBuff);

            if (_pendingList.Count == 0)
                RegisterSend();
        }
    }

    public void Send(ArraySegment<byte> sendBuff)
    {
        lock (_lock)
        {
            _sendQueue.Enqueue(sendBuff);
            if (_pendingList.Count == 0)
                RegisterSend();
        }
    }

    public void Disconnect()
    {
        if (Interlocked.Exchange(ref _disconnected, 1) == 1)
            return;

        OnDisconnected(_socket.RemoteEndPoint);
        _socket.Shutdown(SocketShutdown.Both);
        _socket.Close();
        Clear();
    }

    #region ��Ʈ��ũ ���

    void RegisterSend()
    {
        if (_disconnected == 1)
            return;

        if (_cToken.IsCancellationRequested)
        {
            GameManager.PrintLog("Register is Cancled");

            return;

        }

        while (_sendQueue.Count > 0)
        {
            ArraySegment<byte> buff = _sendQueue.Dequeue();
            _pendingList.Add(buff);
        }
        _sendArgs.BufferList = _pendingList;

        try
        {
            bool pending = _socket.SendAsync(_sendArgs);
            if (pending == false)
                OnSendCompleted(null, _sendArgs);
        }
        catch (Exception e)
        {
            GameManager.PrintLog($"RegisterSend Failed {e}");
        }
    }

    void OnSendCompleted(object sender, SocketAsyncEventArgs args)
    {
        lock (_lock)
        {
           


            if (args.BytesTransferred > 0 && args.SocketError == SocketError.Success)
            {
                try
                {
                    _sendArgs.BufferList = null;
                    _pendingList.Clear();

                    OnSend(_sendArgs.BytesTransferred);

                    if (_sendQueue.Count > 0)
                        RegisterSend();
                }
                catch (Exception e)
                {
                    GameManager.PrintLog($"OnSendCompleted Failed {e}");
                }
            }
            else
            {
                Disconnect();
            }
        }
    }

    void RegisterRecv()
    {
        if (_disconnected == 1)
            return;

        _recvBuffer.Clean();
        ArraySegment<byte> segment = _recvBuffer.WriteSegment;
        _recvArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);

        try
        {
           


            bool pending = _socket.ReceiveAsync(_recvArgs);
            if (pending == false)
                OnRecvCompleted(null, _recvArgs);
        }
        catch (Exception e)
        {
            GameManager.PrintLog($"RegisterRecv Failed {e}");
        }
    }

    void OnRecvCompleted(object sender, SocketAsyncEventArgs args)
    {
        if (args.BytesTransferred > 0 && args.SocketError == SocketError.Success)
        {
            try
            {

                //GameManager.PrintLog($"OnRecvCompleted BytesTransferred : " + args.BytesTransferred);
                // Write Ŀ�� �̵�
                if (_recvBuffer.OnWrite(args.BytesTransferred) == false)
                {
                    Disconnect();
                    return;
                }

                // ������ ������ �����͸� �Ѱ��ְ� �󸶳� ó���ߴ��� �޴´�
                int processLen = OnRecv(_recvBuffer.ReadSegment);
                if (processLen < 0 || _recvBuffer.DataSize < processLen)
                {
                    Disconnect();
                    return;
                }

                // Read Ŀ�� �̵�
                if (_recvBuffer.OnRead(processLen) == false)
                {
                    Disconnect();
                    return;
                }

                //��� ��û�� ���ٸ� Register �����ʰ� �׳� ����
                if(_cToken.IsCancellationRequested)
                {
                    
                    return;
                }


                RegisterRecv();
            }
            catch (Exception e)
            {
                GameManager.PrintLog($"OnRecvCompleted Failed {e}");
            }
        }
        else
        {
            Disconnect();
        }
    }

    #endregion
}

