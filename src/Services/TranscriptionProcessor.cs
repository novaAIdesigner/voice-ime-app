using System;

namespace VoiceImeApp.Services
{
    public class TranscriptionProcessor
    {
        public string ProcessTranscription(string transcription)
        {
            // Format the transcription for injection
            // This could include trimming, correcting casing, etc.
            return transcription.Trim();
        }
    }
}