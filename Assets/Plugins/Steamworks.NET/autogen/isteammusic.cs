// This file is provided under The MIT License as part of Steamworks.NET.
// Copyright (c) 2013-2018 Riley Labrecque
// Please see the included LICENSE.txt for additional information.

// This file is automatically generated.
// Changes to this file will be reverted when you update Steamworks.NET

#if UNITY_ANDROID || UNITY_IOS || UNITY_TIZEN || UNITY_TVOS || UNITY_WEBGL || UNITY_WSA || UNITY_PS4 || UNITY_WII || UNITY_XBOXONE || UNITY_SWITCH
    #define DISABLESTEAMWORKS
#endif

#if !DISABLESTEAMWORKS

using System.Runtime.InteropServices;
using IntPtr = System.IntPtr;

namespace Steamworks
{
    public static class SteamMusic
    {
        public static bool BIsEnabled()
        {
            InteropHelp.TestIfAvailableClient();
            return NativeMethods.ISteamMusic_BIsEnabled(CSteamAPIContext.GetSteamMusic());
        }

        public static bool BIsPlaying()
        {
            InteropHelp.TestIfAvailableClient();
            return NativeMethods.ISteamMusic_BIsPlaying(CSteamAPIContext.GetSteamMusic());
        }

        public static AudioPlayback_Status GetPlaybackStatus()
        {
            InteropHelp.TestIfAvailableClient();
            return NativeMethods.ISteamMusic_GetPlaybackStatus(CSteamAPIContext.GetSteamMusic());
        }

        public static void Play()
        {
            InteropHelp.TestIfAvailableClient();
            NativeMethods.ISteamMusic_Play(CSteamAPIContext.GetSteamMusic());
        }

        public static void Pause()
        {
            InteropHelp.TestIfAvailableClient();
            NativeMethods.ISteamMusic_Pause(CSteamAPIContext.GetSteamMusic());
        }

        public static void PlayPrevious()
        {
            InteropHelp.TestIfAvailableClient();
            NativeMethods.ISteamMusic_PlayPrevious(CSteamAPIContext.GetSteamMusic());
        }

        public static void PlayNext()
        {
            InteropHelp.TestIfAvailableClient();
            NativeMethods.ISteamMusic_PlayNext(CSteamAPIContext.GetSteamMusic());
        }

        /// <summary>
        /// <para> volume is between 0.0 and 1.0</para>
        /// </summary>
        public static void SetVolume(float flVolume)
        {
            InteropHelp.TestIfAvailableClient();
            NativeMethods.ISteamMusic_SetVolume(CSteamAPIContext.GetSteamMusic(), flVolume);
        }

        public static float GetVolume()
        {
            InteropHelp.TestIfAvailableClient();
            return NativeMethods.ISteamMusic_GetVolume(CSteamAPIContext.GetSteamMusic());
        }
    }
}

#endif // !DISABLESTEAMWORKS
