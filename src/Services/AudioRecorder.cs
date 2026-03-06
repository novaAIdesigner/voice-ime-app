using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;

namespace CopilotInput.Services
{
    public class AudioRecorder
    {
        private WaveInEvent waveSource;
        private MemoryStream audioStream;

        public event EventHandler<byte[]> AudioDataAvailable;

        public void StartRecording()
        {
            audioStream = new MemoryStream();
            waveSource = new WaveInEvent();
            waveSource.WaveFormat = new WaveFormat(24000, 16, 1); 
            waveSource.DataAvailable += OnDataAvailable;
            waveSource.StartRecording();
        }

        public async Task<byte[]> StopRecordingAsync()
        {
            if (waveSource == null) return null;

            var tcs = new TaskCompletionSource<bool>();
            waveSource.RecordingStopped += (s, e) => tcs.SetResult(true);

            waveSource.StopRecording();
            await tcs.Task;

            byte[] result = audioStream.ToArray();

            waveSource.Dispose();
            waveSource = null;
            audioStream.Dispose();
            audioStream = null;

            return result;
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            byte[] buffer = new byte[e.BytesRecorded];
            Array.Copy(e.Buffer, buffer, e.BytesRecorded);
            AudioDataAvailable?.Invoke(this, buffer);

            audioStream.Write(e.Buffer, 0, e.BytesRecorded);
        }
    }
}