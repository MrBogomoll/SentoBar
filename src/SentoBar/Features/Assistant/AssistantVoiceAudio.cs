using System;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SentoBar;

/// <summary>Windows default input/output; constructed without opening devices. PCM never leaves memory.</summary>
internal sealed class AssistantVoiceAudio(string microphoneId="",bool localPlayback=false) : IAssistantVoiceAudio
{
    private readonly object gate = new();
    private WaveInEvent? input;
    private IWavePlayer? output;
    private MMDevice? outputDevice;
    private MeteredPcm? pcm;
    private bool disposed;
    public int PlayedMilliseconds => pcm?.PlayedMilliseconds ?? 0;
    public int QueuedMilliseconds => pcm?.QueuedMilliseconds ?? 0;
    internal int PlaybackLatencyMilliseconds=>localPlayback?160:100;
    public void Start(int inputRate, Action<byte[]> capture, Action<float, bool> level, Action failure)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (input != null) throw new InvalidOperationException(Localization.Text("Audio already started."));
            pcm = new MeteredPcm(value => level(value, true),PlaybackLatencyMilliseconds,localPlayback);
            output=InitializeOutput(pcm);
            output.PlaybackStopped += (_, e) => { if (e.Exception != null && !Volatile.Read(ref disposed)) ThreadPool.QueueUserWorkItem(_ => failure()); };
            output.Play();
            input = new WaveInEvent { DeviceNumber = AssistantMicrophones.Resolve(microphoneId), WaveFormat = new WaveFormat(inputRate, 16, 1), BufferMilliseconds = 50, NumberOfBuffers = 3 };
            input.DataAvailable += (_, e) =>
            {
                if (Volatile.Read(ref disposed)) return;
                try
                {
                    byte[] bytes = new byte[e.BytesRecorded]; Buffer.BlockCopy(e.Buffer, 0, bytes, 0, bytes.Length);
                    capture(bytes);
                }
                catch { if (!Volatile.Read(ref disposed)) ThreadPool.QueueUserWorkItem(_ => failure()); }
            };
            input.RecordingStopped += (_, e) => { if (e.Exception != null && !Volatile.Read(ref disposed)) ThreadPool.QueueUserWorkItem(_ => failure()); };
            input.StartRecording();
        }
    }
    private IWavePlayer InitializeOutput(IWaveProvider source)
    {
        if(localPlayback)
        {
            MMDevice? device=null;
            WasapiOut? wasapi=null;
            try
            {
                using var endpoints=new MMDeviceEnumerator();
                device=endpoints.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);
                wasapi=new WasapiOut(device,AudioClientShareMode.Shared,true,PlaybackLatencyMilliseconds);
                wasapi.Init(source);
                outputDevice=device;
                return wasapi;
            }
            catch(Exception ex) when(ex is COMException or InvalidOperationException or NotSupportedException)
            {
                try{wasapi?.Dispose();}finally{device?.Dispose();}
            }
        }
        var waveOut=new WaveOutEvent{DeviceNumber=-1,DesiredLatency=PlaybackLatencyMilliseconds,NumberOfBuffers=2};
        try{waveOut.Init(source);return waveOut;}catch{waveOut.Dispose();throw;}
    }
    public bool Append(byte[] bytes)
    { lock (gate) return !disposed && pcm != null && pcm.Append(bytes); }
    public void Clear()
    {
        lock (gate)
        {
            if (disposed) return;
            // Clear queued PCM while keeping the playback worker alive.
            pcm?.Clear();
        }
    }
    public void Dispose()
    {
        WaveInEvent? mic; IWavePlayer? speaker;MMDevice? device;
        lock (gate)
        {
            if (disposed) return;
            disposed = true; mic = input; input = null; speaker = output; output = null;device=outputDevice;outputDevice=null;pcm?.Clear(); pcm = null;
        }
        try { mic?.StopRecording(); } finally
        {
            try { mic?.Dispose(); } finally
            {
                try { speaker?.Stop(); } finally { try{speaker?.Dispose();}finally{device?.Dispose();} }
            }
        }
    }
    internal static float Peak(byte[] bytes, int count)
    {
        int max = 0;
        for (int i = 0; i + 1 < count; i += 2) max = Math.Max(max, Math.Abs((int)(short)(bytes[i] | bytes[i + 1] << 8)));
        return max / 32768f;
    }
    internal sealed class MeteredPcm(Action<float> level,int nativeLatencyMs=100,bool fadeEdges=false) : IWaveProvider
    {
        private readonly object sync = new();
        // Grow with queued audio instead of reserving two minutes for a short reply.
        // The same 120-second limit, ordering, silence fill and overflow contract apply.
        private const int MaximumBytes=24000*2*120;
        private const int FadeSamples=120;
        private byte[] buffer=Array.Empty<byte>();
        private int head,buffered;
        private int leadingFade=fadeEdges?FadeSamples:0;
        internal int CapacityBytes {get{lock(sync)return buffer.Length;}}
        private long consumed;
        private int generation;
        public WaveFormat WaveFormat {get;}=new(24000,16,1);
        public int PlayedMilliseconds => Math.Max(0, (int)(Interlocked.Read(ref consumed) / 48) - nativeLatencyMs);
        public int QueuedMilliseconds { get { lock (sync) return buffered / 48; } }
        public bool Append(byte[] bytes)
        {
            lock (sync)
            {
                if(bytes.Length>MaximumBytes-buffered)return false;
                int needed=buffered+bytes.Length;
                if(needed>buffer.Length)
                {
                    var grown=new byte[Math.Min(MaximumBytes,Math.Max(needed,Math.Max(48000,buffer.Length*2)))];
                    int first=Math.Min(buffered,buffer.Length-head);
                    if(first>0)Buffer.BlockCopy(buffer,head,grown,0,first);
                    if(buffered>first)Buffer.BlockCopy(buffer,0,grown,first,buffered-first);
                    buffer=grown;head=0;
                }
                if(bytes.Length>0)
                {
                    int tail=(head+buffered)%buffer.Length,first=Math.Min(bytes.Length,buffer.Length-tail);
                    Buffer.BlockCopy(bytes,0,buffer,tail,first);
                    if(bytes.Length>first)Buffer.BlockCopy(bytes,first,buffer,0,bytes.Length-first);
                }
                buffered=needed;return true;
            }
        }
        public void Clear() { lock (sync) { generation++; buffer=Array.Empty<byte>();head=buffered=0;leadingFade=fadeEdges?FadeSamples:0;Interlocked.Exchange(ref consumed, 0); } }
        public int Read(byte[] bytes, int offset, int count)
        {
            int actual, epoch;
            lock (sync)
            {
                epoch=generation;actual=Math.Min(count,buffered);
                if(actual>0)
                {
                    int first=Math.Min(actual,buffer.Length-head);Buffer.BlockCopy(buffer,head,bytes,offset,first);
                    if(actual>first)Buffer.BlockCopy(buffer,0,bytes,offset+first,actual-first);
                    head=(head+actual)%buffer.Length;buffered-=actual;
                    if(fadeEdges)FadeQueueEdges(bytes,offset,actual/2);
                }
                if(actual<count)Array.Clear(bytes,offset+actual,count-actual);
                Interlocked.Add(ref consumed,actual);
            }
            // Never invoke app code on WaveOut's playback thread; Stop may join that thread.
            float peak = 0;
            for (int i = offset; i + 1 < offset + actual; i += 2) peak = Math.Max(peak, Math.Abs((int)(short)(bytes[i] | bytes[i + 1] << 8)) / 32768f);
            float value = peak;
            ThreadPool.QueueUserWorkItem(_ => { if (Volatile.Read(ref generation) == epoch) level(value); });
            return count;
        }
        private void FadeQueueEdges(byte[] bytes,int offset,int samples)
        {
            // Continuous speech stays unchanged. A temporary empty queue tapers into silence
            // and the next sample ramps in, avoiding a hard PCM step at either boundary.
            int begin=Math.Min(samples,leadingFade);
            for(int i=0;i<begin;i++)Scale(bytes,offset+i*2,FadeSamples-leadingFade+i);
            leadingFade-=begin;
            if(buffered>0)return;
            int end=Math.Min(samples,FadeSamples);
            for(int i=0;i<end;i++)Scale(bytes,offset+(samples-1-i)*2,i);
            leadingFade=FadeSamples;
        }
        private static void Scale(byte[] bytes,int at,int gain)
        {
            int sample=(short)(bytes[at] | bytes[at+1]<<8);
            int scaled=sample*gain/FadeSamples;
            bytes[at]=(byte)scaled;
            bytes[at+1]=(byte)(scaled>>8);
        }
    }
}
