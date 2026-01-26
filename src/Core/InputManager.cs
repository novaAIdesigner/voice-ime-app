using System;

namespace VoiceImeApp.Core
{
    public class InputManager
    {
        private bool isListening;

        public void StartListening()
        {
            isListening = true;
            // Logic to start listening for input
        }

        public void StopListening()
        {
            isListening = false;
            // Logic to stop listening for input
        }

        public bool IsListening()
        {
            return isListening;
        }
    }
}