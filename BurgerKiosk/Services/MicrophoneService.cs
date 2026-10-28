using NAudio.Wave;
using System;

namespace BurgerKiosk.Services
{
    public class MicrophoneService : IDisposable
    {
        private WaveInEvent? _waveIn;

        public bool IsRecording { get; private set; }

         
        public event Action<byte[]>? AudioDataAvailable;

         
        public void StartRecording()
        {
            if (IsRecording)
                return;

            if (WaveInEvent.DeviceCount == 0)
            {
                throw new InvalidOperationException(
                    "사용 가능한 마이크가 없습니다.");
            }

            _waveIn = new WaveInEvent
            {
                DeviceNumber = 0,

                 
                WaveFormat = new WaveFormat(16000, 16, 1),

                 
                BufferMilliseconds = 100
            };

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;

            try
            {
                _waveIn.StartRecording();

                IsRecording = true;
            }
            catch
            {
                _waveIn.Dispose();
                _waveIn = null;
                throw;
            }
        }

         
        private void OnDataAvailable(
            object? sender,
            WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0)
                return;

             
            byte[] audioData = new byte[e.BytesRecorded];

            Buffer.BlockCopy(
                e.Buffer,
                0,
                audioData,
                0,
                e.BytesRecorded);

             
            AudioDataAvailable?.Invoke(audioData);
        }

         
        public void StopRecording()
        {
            if (!IsRecording)
                return;

            IsRecording = false;

            _waveIn?.StopRecording();
        }

        private void OnRecordingStopped(
            object? sender,
            StoppedEventArgs e)
        {
            IsRecording = false;

            if (_waveIn != null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStopped;

                _waveIn.Dispose();
                _waveIn = null;
            }
        }

        public void Dispose()
        {
            StopRecording();
        }
    }
}
