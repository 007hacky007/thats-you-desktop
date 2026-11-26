// Managed replacement for the Wwise SWIG P/Invoke table (class AkSoundEnginePINVOKE).
//
// Same class name, entry point names and signatures as the integration expects, so the C#
// Wwise API (AkSoundEngine and the SWIG proxy classes) compiles and runs unchanged. Instead of
// calling into the native AkSoundEngine module, which does not exist for this platform, every
// entry point forwards to the silent backend in PortAudioNull: calls succeed, produce no sound,
// hand out nonzero IDs, and deliver end-of-event and bank callbacks on the next callback pump.
// See PortAudioNull.cs for the behaviour.
//
// The method list was generated mechanically from the entry point signatures; bodies are
// written for this port. A real audio backend can replace these bodies later.

using System;

internal class AkSoundEnginePINVOKE
{
    public static uint CSharp_AK_INVALID_AUX_ID_get()
    {
        return 0u;
    }

    public static uint CSharp_AK_INVALID_CHANNELMASK_get()
    {
        return 0u;
    }

    public static uint CSharp_AK_INVALID_OUTPUT_DEVICE_ID_get()
    {
        return 0xFFFFFFFFu;
    }

    public static uint CSharp_AK_SOUNDBANK_VERSION_get()
    {
        return 113u;
    }

    public static void CSharp_AkExternalSourceInfo_iExternalSrcCookie_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkExternalSourceInfo_iExternalSrcCookie", jarg2);
    }

    public static uint CSharp_AkExternalSourceInfo_iExternalSrcCookie_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkExternalSourceInfo_iExternalSrcCookie");
    }

    public static void CSharp_AkExternalSourceInfo_idCodec_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkExternalSourceInfo_idCodec", jarg2);
    }

    public static uint CSharp_AkExternalSourceInfo_idCodec_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkExternalSourceInfo_idCodec");
    }

    public static void CSharp_AkExternalSourceInfo_szFile_set(IntPtr jarg1, string jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkExternalSourceInfo_szFile", jarg2);
    }

    public static string CSharp_AkExternalSourceInfo_szFile_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<string>(jarg1, "AkExternalSourceInfo_szFile");
    }

    public static void CSharp_AkExternalSourceInfo_pInMemory_set(IntPtr jarg1, IntPtr jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkExternalSourceInfo_pInMemory", jarg2);
    }

    public static IntPtr CSharp_AkExternalSourceInfo_pInMemory_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<IntPtr>(jarg1, "AkExternalSourceInfo_pInMemory");
    }

    public static void CSharp_AkExternalSourceInfo_uiMemorySize_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkExternalSourceInfo_uiMemorySize", jarg2);
    }

    public static uint CSharp_AkExternalSourceInfo_uiMemorySize_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkExternalSourceInfo_uiMemorySize");
    }

    public static void CSharp_AkExternalSourceInfo_idFile_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkExternalSourceInfo_idFile", jarg2);
    }

    public static uint CSharp_AkExternalSourceInfo_idFile_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkExternalSourceInfo_idFile");
    }

    public static IntPtr CSharp_new_AkExternalSourceInfo__SWIG_0()
    {
        return PortAudioNull.NewHandle();
    }

    public static IntPtr CSharp_new_AkExternalSourceInfo__SWIG_1(IntPtr jarg1, uint jarg2, uint jarg3, uint jarg4)
    {
        return PortAudioNull.NewHandle();
    }

    public static IntPtr CSharp_new_AkExternalSourceInfo__SWIG_2(string jarg1, uint jarg2, uint jarg3)
    {
        return PortAudioNull.NewHandle();
    }

    public static IntPtr CSharp_new_AkExternalSourceInfo__SWIG_3(uint jarg1, uint jarg2, uint jarg3)
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkExternalSourceInfo(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static void CSharp_AkVector_X_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkVector_X", jarg2);
    }

    public static float CSharp_AkVector_X_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkVector_X");
    }

    public static void CSharp_AkVector_Y_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkVector_Y", jarg2);
    }

    public static float CSharp_AkVector_Y_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkVector_Y");
    }

    public static void CSharp_AkVector_Z_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkVector_Z", jarg2);
    }

    public static float CSharp_AkVector_Z_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkVector_Z");
    }

    public static IntPtr CSharp_new_AkVector()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkVector(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static void CSharp_AkSoundPosition_Position_set(IntPtr jarg1, IntPtr jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkSoundPosition_Position", jarg2);
    }

    public static IntPtr CSharp_AkSoundPosition_Position_get(IntPtr jarg1)
    {
        return PortAudioNull.GetMember(jarg1, "AkSoundPosition_Position");
    }

    public static void CSharp_AkSoundPosition_Orientation_set(IntPtr jarg1, IntPtr jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkSoundPosition_Orientation", jarg2);
    }

    public static IntPtr CSharp_AkSoundPosition_Orientation_get(IntPtr jarg1)
    {
        return PortAudioNull.GetMember(jarg1, "AkSoundPosition_Orientation");
    }

    public static IntPtr CSharp_new_AkSoundPosition()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkSoundPosition(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static void CSharp_AkListenerPosition_OrientationFront_set(IntPtr jarg1, IntPtr jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkListenerPosition_OrientationFront", jarg2);
    }

    public static IntPtr CSharp_AkListenerPosition_OrientationFront_get(IntPtr jarg1)
    {
        return PortAudioNull.GetMember(jarg1, "AkListenerPosition_OrientationFront");
    }

    public static void CSharp_AkListenerPosition_OrientationTop_set(IntPtr jarg1, IntPtr jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkListenerPosition_OrientationTop", jarg2);
    }

    public static IntPtr CSharp_AkListenerPosition_OrientationTop_get(IntPtr jarg1)
    {
        return PortAudioNull.GetMember(jarg1, "AkListenerPosition_OrientationTop");
    }

    public static void CSharp_AkListenerPosition_Position_set(IntPtr jarg1, IntPtr jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkListenerPosition_Position", jarg2);
    }

    public static IntPtr CSharp_AkListenerPosition_Position_get(IntPtr jarg1)
    {
        return PortAudioNull.GetMember(jarg1, "AkListenerPosition_Position");
    }

    public static IntPtr CSharp_new_AkListenerPosition()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkListenerPosition(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static void CSharp_AkAuxSendValue_auxBusID_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkAuxSendValue_auxBusID", jarg2);
    }

    public static uint CSharp_AkAuxSendValue_auxBusID_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkAuxSendValue_auxBusID");
    }

    public static void CSharp_AkAuxSendValue_fControlValue_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkAuxSendValue_fControlValue", jarg2);
    }

    public static float CSharp_AkAuxSendValue_fControlValue_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkAuxSendValue_fControlValue");
    }

    public static void CSharp_delete_AkAuxSendValue(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static IntPtr CSharp_new_AkRamp__SWIG_0()
    {
        return PortAudioNull.NewHandle();
    }

    public static IntPtr CSharp_new_AkRamp__SWIG_1(float jarg1, float jarg2)
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_AkRamp_fPrev_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkRamp_fPrev", jarg2);
    }

    public static float CSharp_AkRamp_fPrev_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkRamp_fPrev");
    }

    public static void CSharp_AkRamp_fNext_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkRamp_fNext", jarg2);
    }

    public static float CSharp_AkRamp_fNext_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkRamp_fNext");
    }

    public static void CSharp_delete_AkRamp(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static bool CSharp_WwiseObjectIDext_IsEqualTo(IntPtr jarg1, IntPtr jarg2)
    {
        return jarg1 == jarg2;
    }

    public static int CSharp_WwiseObjectIDext_GetNodeType(IntPtr jarg1)
    {
        return 0;
    }

    public static void CSharp_WwiseObjectIDext_id_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "WwiseObjectIDext_id", jarg2);
    }

    public static uint CSharp_WwiseObjectIDext_id_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "WwiseObjectIDext_id");
    }

    public static void CSharp_WwiseObjectIDext_bIsBus_set(IntPtr jarg1, bool jarg2)
    {
        PortAudioNull.SetField(jarg1, "WwiseObjectIDext_bIsBus", jarg2);
    }

    public static bool CSharp_WwiseObjectIDext_bIsBus_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<bool>(jarg1, "WwiseObjectIDext_bIsBus");
    }

    public static IntPtr CSharp_new_WwiseObjectIDext()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_WwiseObjectIDext(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static IntPtr CSharp_new_WwiseObjectID__SWIG_0()
    {
        return PortAudioNull.NewHandle();
    }

    public static IntPtr CSharp_new_WwiseObjectID__SWIG_1(uint jarg1)
    {
        return PortAudioNull.NewHandle();
    }

    public static IntPtr CSharp_new_WwiseObjectID__SWIG_2(uint jarg1, bool jarg2)
    {
        return PortAudioNull.NewHandle();
    }

    public static IntPtr CSharp_new_WwiseObjectID__SWIG_3(uint jarg1, int jarg2)
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_WwiseObjectID(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static void CSharp_EnvelopePoint_uPosition_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "EnvelopePoint_uPosition", jarg2);
    }

    public static uint CSharp_EnvelopePoint_uPosition_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "EnvelopePoint_uPosition");
    }

    public static void CSharp_EnvelopePoint_uAttenuation_set(IntPtr jarg1, ushort jarg2)
    {
        PortAudioNull.SetField(jarg1, "EnvelopePoint_uAttenuation", jarg2);
    }

    public static ushort CSharp_EnvelopePoint_uAttenuation_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<ushort>(jarg1, "EnvelopePoint_uAttenuation");
    }

    public static IntPtr CSharp_new_EnvelopePoint()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_EnvelopePoint(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static ushort CSharp_AK_INT_get()
    {
        return 0;
    }

    public static ushort CSharp_AK_FLOAT_get()
    {
        return 0;
    }

    public static byte CSharp_AK_INTERLEAVED_get()
    {
        return 0;
    }

    public static byte CSharp_AK_NONINTERLEAVED_get()
    {
        return 0;
    }

    public static uint CSharp_AK_LE_NATIVE_BITSPERSAMPLE_get()
    {
        return 32u;
    }

    public static uint CSharp_AK_LE_NATIVE_SAMPLETYPE_get()
    {
        return 1u;
    }

    public static uint CSharp_AK_LE_NATIVE_INTERLEAVE_get()
    {
        return 1u;
    }

    public static void CSharp_AkChannelConfig_uNumChannels_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkChannelConfig_uNumChannels", jarg2);
    }

    public static uint CSharp_AkChannelConfig_uNumChannels_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkChannelConfig_uNumChannels");
    }

    public static void CSharp_AkChannelConfig_eConfigType_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkChannelConfig_eConfigType", jarg2);
    }

    public static uint CSharp_AkChannelConfig_eConfigType_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkChannelConfig_eConfigType");
    }

    public static void CSharp_AkChannelConfig_uChannelMask_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkChannelConfig_uChannelMask", jarg2);
    }

    public static uint CSharp_AkChannelConfig_uChannelMask_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkChannelConfig_uChannelMask");
    }

    public static IntPtr CSharp_new_AkChannelConfig__SWIG_0()
    {
        return PortAudioNull.NewHandle();
    }

    public static IntPtr CSharp_new_AkChannelConfig__SWIG_1(uint jarg1)
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_AkChannelConfig_Clear(IntPtr jarg1)
    {
    }

    public static void CSharp_AkChannelConfig_SetStandard(IntPtr jarg1, uint jarg2)
    {
    }

    public static void CSharp_AkChannelConfig_SetStandardOrAnonymous(IntPtr jarg1, uint jarg2, uint jarg3)
    {
    }

    public static void CSharp_AkChannelConfig_SetAnonymous(IntPtr jarg1, uint jarg2)
    {
    }

    public static void CSharp_AkChannelConfig_SetAmbisonic(IntPtr jarg1, uint jarg2)
    {
    }

    public static bool CSharp_AkChannelConfig_IsValid(IntPtr jarg1)
    {
        return true;
    }

    public static void CSharp_AkChannelConfig_Serialize(IntPtr jarg1, out uint jarg2)
    {
        jarg2 = default(uint);
    }

    public static void CSharp_AkChannelConfig_Deserialize(IntPtr jarg1, uint jarg2)
    {
    }

    public static IntPtr CSharp_AkChannelConfig_RemoveLFE(IntPtr jarg1)
    {
        return PortAudioNull.NewHandle();
    }

    public static IntPtr CSharp_AkChannelConfig_RemoveCenter(IntPtr jarg1)
    {
        return PortAudioNull.NewHandle();
    }

    public static bool CSharp_AkChannelConfig_IsChannelConfigSupported(IntPtr jarg1)
    {
        return true;
    }

    public static void CSharp_delete_AkChannelConfig(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static void CSharp_AkAudioFormat_uSampleRate_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkAudioFormat_uSampleRate", jarg2);
    }

    public static uint CSharp_AkAudioFormat_uSampleRate_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkAudioFormat_uSampleRate");
    }

    public static void CSharp_AkAudioFormat_channelConfig_set(IntPtr jarg1, IntPtr jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkAudioFormat_channelConfig", jarg2);
    }

    public static IntPtr CSharp_AkAudioFormat_channelConfig_get(IntPtr jarg1)
    {
        return PortAudioNull.GetMember(jarg1, "AkAudioFormat_channelConfig");
    }

    public static void CSharp_AkAudioFormat_uBitsPerSample_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkAudioFormat_uBitsPerSample", jarg2);
    }

    public static uint CSharp_AkAudioFormat_uBitsPerSample_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkAudioFormat_uBitsPerSample");
    }

    public static void CSharp_AkAudioFormat_uBlockAlign_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkAudioFormat_uBlockAlign", jarg2);
    }

    public static uint CSharp_AkAudioFormat_uBlockAlign_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkAudioFormat_uBlockAlign");
    }

    public static void CSharp_AkAudioFormat_uTypeID_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkAudioFormat_uTypeID", jarg2);
    }

    public static uint CSharp_AkAudioFormat_uTypeID_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkAudioFormat_uTypeID");
    }

    public static void CSharp_AkAudioFormat_uInterleaveID_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkAudioFormat_uInterleaveID", jarg2);
    }

    public static uint CSharp_AkAudioFormat_uInterleaveID_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkAudioFormat_uInterleaveID");
    }

    public static uint CSharp_AkAudioFormat_GetNumChannels(IntPtr jarg1)
    {
        return 0u;
    }

    public static uint CSharp_AkAudioFormat_GetBitsPerSample(IntPtr jarg1)
    {
        return 0u;
    }

    public static uint CSharp_AkAudioFormat_GetBlockAlign(IntPtr jarg1)
    {
        return 0u;
    }

    public static uint CSharp_AkAudioFormat_GetTypeID(IntPtr jarg1)
    {
        return 0u;
    }

    public static uint CSharp_AkAudioFormat_GetInterleaveID(IntPtr jarg1)
    {
        return 0u;
    }

    public static void CSharp_AkAudioFormat_SetAll(IntPtr jarg1, uint jarg2, IntPtr jarg3, uint jarg4, uint jarg5, uint jarg6, uint jarg7)
    {
    }

    public static bool CSharp_AkAudioFormat_IsChannelConfigSupported(IntPtr jarg1)
    {
        return true;
    }

    public static IntPtr CSharp_new_AkAudioFormat()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkAudioFormat(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static void CSharp_Iterator_pItem_set(IntPtr jarg1, IntPtr jarg2)
    {
        PortAudioNull.SetField(jarg1, "Iterator_pItem", jarg2);
    }

    public static IntPtr CSharp_Iterator_pItem_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<IntPtr>(jarg1, "Iterator_pItem");
    }

    public static IntPtr CSharp_Iterator_NextIter(IntPtr jarg1)
    {
        return IntPtr.Zero;
    }

    public static IntPtr CSharp_Iterator_PrevIter(IntPtr jarg1)
    {
        return IntPtr.Zero;
    }

    public static IntPtr CSharp_Iterator_GetItem(IntPtr jarg1)
    {
        return IntPtr.Zero;
    }

    public static bool CSharp_Iterator_IsEqualTo(IntPtr jarg1, IntPtr jarg2)
    {
        return jarg1 == jarg2;
    }

    public static bool CSharp_Iterator_IsDifferentFrom(IntPtr jarg1, IntPtr jarg2)
    {
        return jarg1 != jarg2;
    }

    public static IntPtr CSharp_new_Iterator()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_Iterator(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static int CSharp__ArrayPoolDefault_Get()
    {
        return 0;
    }

    public static IntPtr CSharp_new__ArrayPoolDefault()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete__ArrayPoolDefault(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static int CSharp__ArrayPoolLEngineDefault_Get()
    {
        return 0;
    }

    public static IntPtr CSharp_new__ArrayPoolLEngineDefault()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete__ArrayPoolLEngineDefault(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static IntPtr CSharp_new_PlaylistItem__SWIG_0()
    {
        return PortAudioNull.NewHandle();
    }

    public static IntPtr CSharp_new_PlaylistItem__SWIG_1(IntPtr jarg1)
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_PlaylistItem(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static IntPtr CSharp_PlaylistItem_Assign(IntPtr jarg1, IntPtr jarg2)
    {
        return jarg1;
    }

    public static bool CSharp_PlaylistItem_IsEqualTo(IntPtr jarg1, IntPtr jarg2)
    {
        return jarg1 == jarg2;
    }

    public static int CSharp_PlaylistItem_SetExternalSources(IntPtr jarg1, uint jarg2, IntPtr jarg3)
    {
        return PortAudioNull.Success;
    }

    public static void CSharp_PlaylistItem_audioNodeID_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "PlaylistItem_audioNodeID", jarg2);
    }

    public static uint CSharp_PlaylistItem_audioNodeID_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "PlaylistItem_audioNodeID");
    }

    public static void CSharp_PlaylistItem_msDelay_set(IntPtr jarg1, int jarg2)
    {
        PortAudioNull.SetField(jarg1, "PlaylistItem_msDelay", jarg2);
    }

    public static int CSharp_PlaylistItem_msDelay_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<int>(jarg1, "PlaylistItem_msDelay");
    }

    public static void CSharp_PlaylistItem_pCustomInfo_set(IntPtr jarg1, IntPtr jarg2)
    {
        PortAudioNull.SetField(jarg1, "PlaylistItem_pCustomInfo", jarg2);
    }

    public static IntPtr CSharp_PlaylistItem_pCustomInfo_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<IntPtr>(jarg1, "PlaylistItem_pCustomInfo");
    }

    public static IntPtr CSharp_new_AkPlaylistArray()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkPlaylistArray(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static IntPtr CSharp_AkPlaylistArray_Begin(IntPtr jarg1)
    {
        return IntPtr.Zero;
    }

    public static IntPtr CSharp_AkPlaylistArray_End(IntPtr jarg1)
    {
        return IntPtr.Zero;
    }

    public static IntPtr CSharp_AkPlaylistArray_FindEx(IntPtr jarg1, IntPtr jarg2)
    {
        return IntPtr.Zero;
    }

    public static IntPtr CSharp_AkPlaylistArray_Erase__SWIG_0(IntPtr jarg1, IntPtr jarg2)
    {
        return IntPtr.Zero;
    }

    public static void CSharp_AkPlaylistArray_Erase__SWIG_1(IntPtr jarg1, uint jarg2)
    {
    }

    public static IntPtr CSharp_AkPlaylistArray_EraseSwap(IntPtr jarg1, IntPtr jarg2)
    {
        return IntPtr.Zero;
    }

    public static int CSharp_AkPlaylistArray_Reserve(IntPtr jarg1, uint jarg2)
    {
        return PortAudioNull.Success;
    }

    public static uint CSharp_AkPlaylistArray_Reserved(IntPtr jarg1)
    {
        return 0u;
    }

    public static void CSharp_AkPlaylistArray_Term(IntPtr jarg1)
    {
    }

    public static uint CSharp_AkPlaylistArray_Length(IntPtr jarg1)
    {
        return 0u;
    }

    public static bool CSharp_AkPlaylistArray_IsEmpty(IntPtr jarg1)
    {
        return false;
    }

    public static IntPtr CSharp_AkPlaylistArray_Exists(IntPtr jarg1, IntPtr jarg2)
    {
        return IntPtr.Zero;
    }

    public static IntPtr CSharp_AkPlaylistArray_AddLast__SWIG_0(IntPtr jarg1)
    {
        return IntPtr.Zero;
    }

    public static IntPtr CSharp_AkPlaylistArray_AddLast__SWIG_1(IntPtr jarg1, IntPtr jarg2)
    {
        return IntPtr.Zero;
    }

    public static IntPtr CSharp_AkPlaylistArray_Last(IntPtr jarg1)
    {
        return IntPtr.Zero;
    }

    public static void CSharp_AkPlaylistArray_RemoveLast(IntPtr jarg1)
    {
    }

    public static int CSharp_AkPlaylistArray_Remove(IntPtr jarg1, IntPtr jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_AkPlaylistArray_RemoveSwap(IntPtr jarg1, IntPtr jarg2)
    {
        return PortAudioNull.Success;
    }

    public static void CSharp_AkPlaylistArray_RemoveAll(IntPtr jarg1)
    {
    }

    public static IntPtr CSharp_AkPlaylistArray_ItemAtIndex(IntPtr jarg1, uint jarg2)
    {
        return IntPtr.Zero;
    }

    public static IntPtr CSharp_AkPlaylistArray_Insert(IntPtr jarg1, uint jarg2)
    {
        return IntPtr.Zero;
    }

    public static bool CSharp_AkPlaylistArray_GrowArray__SWIG_0(IntPtr jarg1, uint jarg2)
    {
        return false;
    }

    public static bool CSharp_AkPlaylistArray_GrowArray__SWIG_1(IntPtr jarg1)
    {
        return false;
    }

    public static bool CSharp_AkPlaylistArray_Resize(IntPtr jarg1, uint jarg2)
    {
        return false;
    }

    public static void CSharp_AkPlaylistArray_Transfer(IntPtr jarg1, IntPtr jarg2)
    {
    }

    public static int CSharp_Playlist_Enqueue__SWIG_0(IntPtr jarg1, uint jarg2, int jarg3, IntPtr jarg4, uint jarg5, IntPtr jarg6)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_Playlist_Enqueue__SWIG_1(IntPtr jarg1, uint jarg2, int jarg3, IntPtr jarg4, uint jarg5)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_Playlist_Enqueue__SWIG_2(IntPtr jarg1, uint jarg2, int jarg3, IntPtr jarg4)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_Playlist_Enqueue__SWIG_3(IntPtr jarg1, uint jarg2, int jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_Playlist_Enqueue__SWIG_4(IntPtr jarg1, uint jarg2)
    {
        return PortAudioNull.Success;
    }

    public static IntPtr CSharp_new_Playlist()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_Playlist(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static uint CSharp_DynamicSequenceOpen__SWIG_0(ulong jarg1, uint jarg2, IntPtr jarg3, IntPtr jarg4, int jarg5)
    {
        return PortAudioNull.NewId();
    }

    public static uint CSharp_DynamicSequenceOpen__SWIG_1(ulong jarg1, uint jarg2, IntPtr jarg3, IntPtr jarg4)
    {
        return PortAudioNull.NewId();
    }

    public static uint CSharp_DynamicSequenceOpen__SWIG_2(ulong jarg1, uint jarg2)
    {
        return PortAudioNull.NewId();
    }

    public static uint CSharp_DynamicSequenceOpen__SWIG_3(ulong jarg1)
    {
        return PortAudioNull.NewId();
    }

    public static int CSharp_DynamicSequenceClose(uint jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_DynamicSequencePlay__SWIG_0(uint jarg1, int jarg2, int jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_DynamicSequencePlay__SWIG_1(uint jarg1, int jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_DynamicSequencePlay__SWIG_2(uint jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_DynamicSequencePause__SWIG_0(uint jarg1, int jarg2, int jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_DynamicSequencePause__SWIG_1(uint jarg1, int jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_DynamicSequencePause__SWIG_2(uint jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_DynamicSequenceResume__SWIG_0(uint jarg1, int jarg2, int jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_DynamicSequenceResume__SWIG_1(uint jarg1, int jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_DynamicSequenceResume__SWIG_2(uint jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_DynamicSequenceStop__SWIG_0(uint jarg1, int jarg2, int jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_DynamicSequenceStop__SWIG_1(uint jarg1, int jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_DynamicSequenceStop__SWIG_2(uint jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_DynamicSequenceBreak(uint jarg1)
    {
        return PortAudioNull.Success;
    }

    public static IntPtr CSharp_DynamicSequenceLockPlaylist(uint jarg1)
    {
        return PortAudioNull.NewHandle();
    }

    public static int CSharp_DynamicSequenceUnlockPlaylist(uint jarg1)
    {
        return PortAudioNull.Success;
    }

    public static void CSharp_AkOutputSettings_ePanningRule_set(IntPtr jarg1, int jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkOutputSettings_ePanningRule", jarg2);
    }

    public static int CSharp_AkOutputSettings_ePanningRule_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<int>(jarg1, "AkOutputSettings_ePanningRule");
    }

    public static void CSharp_AkOutputSettings_channelConfig_set(IntPtr jarg1, IntPtr jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkOutputSettings_channelConfig", jarg2);
    }

    public static IntPtr CSharp_AkOutputSettings_channelConfig_get(IntPtr jarg1)
    {
        return PortAudioNull.GetMember(jarg1, "AkOutputSettings_channelConfig");
    }

    public static IntPtr CSharp_new_AkOutputSettings()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkOutputSettings(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static void CSharp_AkInitSettings_pfnAssertHook_set(IntPtr jarg1, IntPtr jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkInitSettings_pfnAssertHook", jarg2);
    }

    public static IntPtr CSharp_AkInitSettings_pfnAssertHook_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<IntPtr>(jarg1, "AkInitSettings_pfnAssertHook");
    }

    public static void CSharp_AkInitSettings_uMaxNumPaths_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkInitSettings_uMaxNumPaths", jarg2);
    }

    public static uint CSharp_AkInitSettings_uMaxNumPaths_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkInitSettings_uMaxNumPaths");
    }

    public static void CSharp_AkInitSettings_uMaxNumTransitions_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkInitSettings_uMaxNumTransitions", jarg2);
    }

    public static uint CSharp_AkInitSettings_uMaxNumTransitions_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkInitSettings_uMaxNumTransitions");
    }

    public static void CSharp_AkInitSettings_uDefaultPoolSize_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkInitSettings_uDefaultPoolSize", jarg2);
    }

    public static uint CSharp_AkInitSettings_uDefaultPoolSize_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkInitSettings_uDefaultPoolSize");
    }

    public static void CSharp_AkInitSettings_fDefaultPoolRatioThreshold_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkInitSettings_fDefaultPoolRatioThreshold", jarg2);
    }

    public static float CSharp_AkInitSettings_fDefaultPoolRatioThreshold_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkInitSettings_fDefaultPoolRatioThreshold");
    }

    public static void CSharp_AkInitSettings_uCommandQueueSize_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkInitSettings_uCommandQueueSize", jarg2);
    }

    public static uint CSharp_AkInitSettings_uCommandQueueSize_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkInitSettings_uCommandQueueSize");
    }

    public static void CSharp_AkInitSettings_uPrepareEventMemoryPoolID_set(IntPtr jarg1, int jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkInitSettings_uPrepareEventMemoryPoolID", jarg2);
    }

    public static int CSharp_AkInitSettings_uPrepareEventMemoryPoolID_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<int>(jarg1, "AkInitSettings_uPrepareEventMemoryPoolID");
    }

    public static void CSharp_AkInitSettings_bEnableGameSyncPreparation_set(IntPtr jarg1, bool jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkInitSettings_bEnableGameSyncPreparation", jarg2);
    }

    public static bool CSharp_AkInitSettings_bEnableGameSyncPreparation_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<bool>(jarg1, "AkInitSettings_bEnableGameSyncPreparation");
    }

    public static void CSharp_AkInitSettings_uContinuousPlaybackLookAhead_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkInitSettings_uContinuousPlaybackLookAhead", jarg2);
    }

    public static uint CSharp_AkInitSettings_uContinuousPlaybackLookAhead_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkInitSettings_uContinuousPlaybackLookAhead");
    }

    public static void CSharp_AkInitSettings_uNumSamplesPerFrame_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkInitSettings_uNumSamplesPerFrame", jarg2);
    }

    public static uint CSharp_AkInitSettings_uNumSamplesPerFrame_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkInitSettings_uNumSamplesPerFrame");
    }

    public static void CSharp_AkInitSettings_uMonitorPoolSize_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkInitSettings_uMonitorPoolSize", jarg2);
    }

    public static uint CSharp_AkInitSettings_uMonitorPoolSize_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkInitSettings_uMonitorPoolSize");
    }

    public static void CSharp_AkInitSettings_uMonitorQueuePoolSize_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkInitSettings_uMonitorQueuePoolSize", jarg2);
    }

    public static uint CSharp_AkInitSettings_uMonitorQueuePoolSize_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkInitSettings_uMonitorQueuePoolSize");
    }

    public static void CSharp_AkInitSettings_eMainOutputType_set(IntPtr jarg1, int jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkInitSettings_eMainOutputType", jarg2);
    }

    public static int CSharp_AkInitSettings_eMainOutputType_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<int>(jarg1, "AkInitSettings_eMainOutputType");
    }

    public static void CSharp_AkInitSettings_settingsMainOutput_set(IntPtr jarg1, IntPtr jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkInitSettings_settingsMainOutput", jarg2);
    }

    public static IntPtr CSharp_AkInitSettings_settingsMainOutput_get(IntPtr jarg1)
    {
        return PortAudioNull.GetMember(jarg1, "AkInitSettings_settingsMainOutput");
    }

    public static void CSharp_AkInitSettings_uMaxHardwareTimeoutMs_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkInitSettings_uMaxHardwareTimeoutMs", jarg2);
    }

    public static uint CSharp_AkInitSettings_uMaxHardwareTimeoutMs_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkInitSettings_uMaxHardwareTimeoutMs");
    }

    public static void CSharp_AkInitSettings_bUseSoundBankMgrThread_set(IntPtr jarg1, bool jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkInitSettings_bUseSoundBankMgrThread", jarg2);
    }

    public static bool CSharp_AkInitSettings_bUseSoundBankMgrThread_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<bool>(jarg1, "AkInitSettings_bUseSoundBankMgrThread");
    }

    public static void CSharp_AkInitSettings_bUseLEngineThread_set(IntPtr jarg1, bool jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkInitSettings_bUseLEngineThread", jarg2);
    }

    public static bool CSharp_AkInitSettings_bUseLEngineThread_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<bool>(jarg1, "AkInitSettings_bUseLEngineThread");
    }

    public static IntPtr CSharp_new_AkInitSettings()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkInitSettings(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static void CSharp_AkSourceSettings_sourceID_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkSourceSettings_sourceID", jarg2);
    }

    public static uint CSharp_AkSourceSettings_sourceID_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkSourceSettings_sourceID");
    }

    public static void CSharp_AkSourceSettings_pMediaMemory_set(IntPtr jarg1, IntPtr jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkSourceSettings_pMediaMemory", jarg2);
    }

    public static IntPtr CSharp_AkSourceSettings_pMediaMemory_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<IntPtr>(jarg1, "AkSourceSettings_pMediaMemory");
    }

    public static void CSharp_AkSourceSettings_uMediaSize_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkSourceSettings_uMediaSize", jarg2);
    }

    public static uint CSharp_AkSourceSettings_uMediaSize_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkSourceSettings_uMediaSize");
    }

    public static IntPtr CSharp_new_AkSourceSettings()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkSourceSettings(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static IntPtr CSharp_GetSpeakerConfiguration__SWIG_0(int jarg1, uint jarg2)
    {
        return PortAudioNull.NewHandle();
    }

    public static IntPtr CSharp_GetSpeakerConfiguration__SWIG_1(int jarg1)
    {
        return PortAudioNull.NewHandle();
    }

    public static IntPtr CSharp_GetSpeakerConfiguration__SWIG_2()
    {
        return PortAudioNull.NewHandle();
    }

    public static int CSharp_GetPanningRule__SWIG_0(out int jarg1, int jarg2, uint jarg3)
    {
        jarg1 = default(int);
        return PortAudioNull.Success;
    }

    public static int CSharp_GetPanningRule__SWIG_1(out int jarg1, int jarg2)
    {
        jarg1 = default(int);
        return PortAudioNull.Success;
    }

    public static int CSharp_GetPanningRule__SWIG_2(out int jarg1)
    {
        jarg1 = default(int);
        return PortAudioNull.Success;
    }

    public static int CSharp_SetPanningRule__SWIG_0(int jarg1, int jarg2, uint jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetPanningRule__SWIG_1(int jarg1, int jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetPanningRule__SWIG_2(int jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_GetSpeakerAngles__SWIG_0(float[] jarg1, ref uint jarg2, out float jarg3, int jarg4, uint jarg5)
    {
        jarg2 = 0u;
        jarg3 = default(float);
        return PortAudioNull.Success;
    }

    public static int CSharp_GetSpeakerAngles__SWIG_1(float[] jarg1, ref uint jarg2, out float jarg3, int jarg4)
    {
        jarg2 = 0u;
        jarg3 = default(float);
        return PortAudioNull.Success;
    }

    public static int CSharp_GetSpeakerAngles__SWIG_2(float[] jarg1, ref uint jarg2, out float jarg3)
    {
        jarg2 = 0u;
        jarg3 = default(float);
        return PortAudioNull.Success;
    }

    public static int CSharp_SetSpeakerAngles__SWIG_0(float[] jarg1, uint jarg2, float jarg3, int jarg4, uint jarg5)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetSpeakerAngles__SWIG_1(float[] jarg1, uint jarg2, float jarg3, int jarg4)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetSpeakerAngles__SWIG_2(float[] jarg1, uint jarg2, float jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetVolumeThreshold(float jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetMaxNumVoicesLimit(ushort jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_RenderAudio()
    {
        return PortAudioNull.Success;
    }

    public static uint CSharp_GetIDFromString__SWIG_0(string jarg1)
    {
        return PortAudioNull.HashName(jarg1);
    }

    public static uint CSharp_PostEvent__SWIG_0(uint jarg1, ulong jarg2, uint jarg3, IntPtr jarg4, IntPtr jarg5, uint jarg6, IntPtr jarg7, uint jarg8)
    {
        return PortAudioNull.PostEvent(jarg1, jarg2, jarg3, jarg4, jarg5, jarg8);
    }

    public static uint CSharp_PostEvent__SWIG_1(uint jarg1, ulong jarg2, uint jarg3, IntPtr jarg4, IntPtr jarg5, uint jarg6, IntPtr jarg7)
    {
        return PortAudioNull.PostEvent(jarg1, jarg2, jarg3, jarg4, jarg5, 0u);
    }

    public static uint CSharp_PostEvent__SWIG_2(uint jarg1, ulong jarg2, uint jarg3, IntPtr jarg4, IntPtr jarg5, uint jarg6)
    {
        return PortAudioNull.PostEvent(jarg1, jarg2, jarg3, jarg4, jarg5, 0u);
    }

    public static uint CSharp_PostEvent__SWIG_3(uint jarg1, ulong jarg2, uint jarg3, IntPtr jarg4, IntPtr jarg5)
    {
        return PortAudioNull.PostEvent(jarg1, jarg2, jarg3, jarg4, jarg5, 0u);
    }

    public static uint CSharp_PostEvent__SWIG_4(uint jarg1, ulong jarg2, uint jarg3)
    {
        return PortAudioNull.PostEvent(jarg1, jarg2, 0u, IntPtr.Zero, IntPtr.Zero, 0u);
    }

    public static uint CSharp_PostEvent__SWIG_5(uint jarg1, ulong jarg2)
    {
        return PortAudioNull.PostEvent(jarg1, jarg2, 0u, IntPtr.Zero, IntPtr.Zero, 0u);
    }

    public static uint CSharp_PostEvent__SWIG_6(string jarg1, ulong jarg2, uint jarg3, IntPtr jarg4, IntPtr jarg5, uint jarg6, IntPtr jarg7, uint jarg8)
    {
        return PortAudioNull.PostEvent(jarg1, jarg2, jarg3, jarg4, jarg5, jarg8);
    }

    public static uint CSharp_PostEvent__SWIG_7(string jarg1, ulong jarg2, uint jarg3, IntPtr jarg4, IntPtr jarg5, uint jarg6, IntPtr jarg7)
    {
        return PortAudioNull.PostEvent(jarg1, jarg2, jarg3, jarg4, jarg5, 0u);
    }

    public static uint CSharp_PostEvent__SWIG_8(string jarg1, ulong jarg2, uint jarg3, IntPtr jarg4, IntPtr jarg5, uint jarg6)
    {
        return PortAudioNull.PostEvent(jarg1, jarg2, jarg3, jarg4, jarg5, 0u);
    }

    public static uint CSharp_PostEvent__SWIG_9(string jarg1, ulong jarg2, uint jarg3, IntPtr jarg4, IntPtr jarg5)
    {
        return PortAudioNull.PostEvent(jarg1, jarg2, jarg3, jarg4, jarg5, 0u);
    }

    public static uint CSharp_PostEvent__SWIG_10(string jarg1, ulong jarg2, uint jarg3)
    {
        return PortAudioNull.PostEvent(jarg1, jarg2, 0u, IntPtr.Zero, IntPtr.Zero, 0u);
    }

    public static uint CSharp_PostEvent__SWIG_11(string jarg1, ulong jarg2)
    {
        return PortAudioNull.PostEvent(jarg1, jarg2, 0u, IntPtr.Zero, IntPtr.Zero, 0u);
    }

    public static int CSharp_ExecuteActionOnEvent__SWIG_0(uint jarg1, int jarg2, ulong jarg3, int jarg4, int jarg5, uint jarg6)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ExecuteActionOnEvent__SWIG_1(uint jarg1, int jarg2, ulong jarg3, int jarg4, int jarg5)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ExecuteActionOnEvent__SWIG_2(uint jarg1, int jarg2, ulong jarg3, int jarg4)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ExecuteActionOnEvent__SWIG_3(uint jarg1, int jarg2, ulong jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ExecuteActionOnEvent__SWIG_4(uint jarg1, int jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ExecuteActionOnEvent__SWIG_5(string jarg1, int jarg2, ulong jarg3, int jarg4, int jarg5, uint jarg6)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ExecuteActionOnEvent__SWIG_6(string jarg1, int jarg2, ulong jarg3, int jarg4, int jarg5)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ExecuteActionOnEvent__SWIG_7(string jarg1, int jarg2, ulong jarg3, int jarg4)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ExecuteActionOnEvent__SWIG_8(string jarg1, int jarg2, ulong jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ExecuteActionOnEvent__SWIG_9(string jarg1, int jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_PinEventInStreamCache__SWIG_0(uint jarg1, sbyte jarg2, sbyte jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_PinEventInStreamCache__SWIG_1(string jarg1, sbyte jarg2, sbyte jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_UnpinEventInStreamCache__SWIG_0(uint jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_UnpinEventInStreamCache__SWIG_1(string jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_GetBufferStatusForPinnedEvent__SWIG_0(uint jarg1, out float jarg2, out int jarg3)
    {
        jarg2 = default(float);
        jarg3 = default(int);
        return PortAudioNull.Success;
    }

    public static int CSharp_GetBufferStatusForPinnedEvent__SWIG_1(string jarg1, out float jarg2, out int jarg3)
    {
        jarg2 = default(float);
        jarg3 = default(int);
        return PortAudioNull.Success;
    }

    public static int CSharp_SeekOnEvent__SWIG_0(uint jarg1, ulong jarg2, int jarg3, bool jarg4, uint jarg5)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SeekOnEvent__SWIG_1(uint jarg1, ulong jarg2, int jarg3, bool jarg4)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SeekOnEvent__SWIG_2(uint jarg1, ulong jarg2, int jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SeekOnEvent__SWIG_3(string jarg1, ulong jarg2, int jarg3, bool jarg4, uint jarg5)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SeekOnEvent__SWIG_4(string jarg1, ulong jarg2, int jarg3, bool jarg4)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SeekOnEvent__SWIG_5(string jarg1, ulong jarg2, int jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SeekOnEvent__SWIG_9(uint jarg1, ulong jarg2, float jarg3, bool jarg4, uint jarg5)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SeekOnEvent__SWIG_10(uint jarg1, ulong jarg2, float jarg3, bool jarg4)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SeekOnEvent__SWIG_11(uint jarg1, ulong jarg2, float jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SeekOnEvent__SWIG_12(string jarg1, ulong jarg2, float jarg3, bool jarg4, uint jarg5)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SeekOnEvent__SWIG_13(string jarg1, ulong jarg2, float jarg3, bool jarg4)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SeekOnEvent__SWIG_14(string jarg1, ulong jarg2, float jarg3)
    {
        return PortAudioNull.Success;
    }

    public static void CSharp_CancelEventCallbackCookie(IntPtr jarg1)
    {
    }

    public static void CSharp_CancelEventCallback(uint jarg1)
    {
    }

    public static int CSharp_GetSourcePlayPosition__SWIG_0(uint jarg1, out int jarg2, bool jarg3)
    {
        jarg2 = default(int);
        return PortAudioNull.Success;
    }

    public static int CSharp_GetSourcePlayPosition__SWIG_1(uint jarg1, out int jarg2)
    {
        jarg2 = default(int);
        return PortAudioNull.Success;
    }

    public static int CSharp_GetSourceStreamBuffering(uint jarg1, out int jarg2, out int jarg3)
    {
        jarg2 = default(int);
        jarg3 = default(int);
        return PortAudioNull.Success;
    }

    public static void CSharp_StopAll__SWIG_0(ulong jarg1)
    {
    }

    public static void CSharp_StopAll__SWIG_1()
    {
    }

    public static void CSharp_StopPlayingID__SWIG_0(uint jarg1, int jarg2, int jarg3)
    {
    }

    public static void CSharp_StopPlayingID__SWIG_1(uint jarg1, int jarg2)
    {
    }

    public static void CSharp_StopPlayingID__SWIG_2(uint jarg1)
    {
    }

    public static void CSharp_SetRandomSeed(uint jarg1)
    {
    }

    public static void CSharp_MuteBackgroundMusic(bool jarg1)
    {
    }

    public static int CSharp_UnregisterAllGameObj()
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetMultiplePositions__SWIG_0(ulong jarg1, IntPtr jarg2, ushort jarg3, int jarg4)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetMultiplePositions__SWIG_1(ulong jarg1, IntPtr jarg2, ushort jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetAttenuationScalingFactor(ulong jarg1, float jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetListenerScalingFactor(uint jarg1, float jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ClearBanks()
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetBankLoadIOSettings(float jarg1, sbyte jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_LoadBank__SWIG_0(string jarg1, int jarg2, out uint jarg3)
    {
        jarg3 = PortAudioNull.BankIdFromName(jarg1);
        return PortAudioNull.Success;
    }

    public static int CSharp_LoadBank__SWIG_1(uint jarg1, int jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_LoadBank__SWIG_2(IntPtr jarg1, uint jarg2, out uint jarg3)
    {
        jarg3 = PortAudioNull.NewId();
        return PortAudioNull.Success;
    }

    public static int CSharp_LoadBank__SWIG_3(IntPtr jarg1, uint jarg2, int jarg3, out uint jarg4)
    {
        jarg4 = PortAudioNull.NewId();
        return PortAudioNull.Success;
    }

    public static int CSharp_LoadBank__SWIG_4(string jarg1, IntPtr jarg2, IntPtr jarg3, int jarg4, out uint jarg5)
    {
        jarg5 = PortAudioNull.BankIdFromName(jarg1);
        return PortAudioNull.BankRequest(jarg5, jarg3);
    }

    public static int CSharp_LoadBank__SWIG_5(uint jarg1, IntPtr jarg2, IntPtr jarg3, int jarg4)
    {
        return PortAudioNull.BankRequest(jarg1, jarg3);
    }

    public static int CSharp_LoadBank__SWIG_6(IntPtr jarg1, uint jarg2, IntPtr jarg3, IntPtr jarg4, out uint jarg5)
    {
        jarg5 = PortAudioNull.NewId();
        return PortAudioNull.BankRequest(jarg5, jarg4);
    }

    public static int CSharp_LoadBank__SWIG_7(IntPtr jarg1, uint jarg2, IntPtr jarg3, IntPtr jarg4, int jarg5, out uint jarg6)
    {
        jarg6 = PortAudioNull.NewId();
        return PortAudioNull.BankRequest(jarg6, jarg4);
    }

    public static int CSharp_UnloadBank__SWIG_0(string jarg1, IntPtr jarg2, out int jarg3)
    {
        jarg3 = PortAudioNull.InvalidPoolId;
        return PortAudioNull.Success;
    }

    public static int CSharp_UnloadBank__SWIG_1(string jarg1, IntPtr jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_UnloadBank__SWIG_4(uint jarg1, IntPtr jarg2, out int jarg3)
    {
        jarg3 = PortAudioNull.InvalidPoolId;
        return PortAudioNull.Success;
    }

    public static int CSharp_UnloadBank__SWIG_5(uint jarg1, IntPtr jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_UnloadBank__SWIG_6(string jarg1, IntPtr jarg2, IntPtr jarg3, IntPtr jarg4)
    {
        return PortAudioNull.BankRequest(PortAudioNull.BankIdFromName(jarg1), jarg4);
    }

    public static int CSharp_UnloadBank__SWIG_8(uint jarg1, IntPtr jarg2, IntPtr jarg3, IntPtr jarg4)
    {
        return PortAudioNull.BankRequest(jarg1, jarg4);
    }

    public static void CSharp_CancelBankCallbackCookie(IntPtr jarg1)
    {
    }

    public static int CSharp_PrepareBank__SWIG_0(int jarg1, string jarg2, int jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_PrepareBank__SWIG_1(int jarg1, string jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_PrepareBank__SWIG_4(int jarg1, uint jarg2, int jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_PrepareBank__SWIG_5(int jarg1, uint jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_PrepareBank__SWIG_6(int jarg1, string jarg2, IntPtr jarg3, IntPtr jarg4, int jarg5)
    {
        return PortAudioNull.BankRequest(PortAudioNull.BankIdFromName(jarg2), jarg4);
    }

    public static int CSharp_PrepareBank__SWIG_7(int jarg1, string jarg2, IntPtr jarg3, IntPtr jarg4)
    {
        return PortAudioNull.BankRequest(PortAudioNull.BankIdFromName(jarg2), jarg4);
    }

    public static int CSharp_PrepareBank__SWIG_10(int jarg1, uint jarg2, IntPtr jarg3, IntPtr jarg4, int jarg5)
    {
        return PortAudioNull.BankRequest(jarg2, jarg4);
    }

    public static int CSharp_PrepareBank__SWIG_11(int jarg1, uint jarg2, IntPtr jarg3, IntPtr jarg4)
    {
        return PortAudioNull.BankRequest(jarg2, jarg4);
    }

    public static int CSharp_ClearPreparedEvents()
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_PrepareEvent__SWIG_0(int jarg1, IntPtr jarg2, uint jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_PrepareEvent__SWIG_1(int jarg1, uint[] jarg2, uint jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_PrepareEvent__SWIG_2(int jarg1, IntPtr jarg2, uint jarg3, IntPtr jarg4, IntPtr jarg5)
    {
        return PortAudioNull.BankRequest(0u, jarg5);
    }

    public static int CSharp_PrepareEvent__SWIG_3(int jarg1, uint[] jarg2, uint jarg3, IntPtr jarg4, IntPtr jarg5)
    {
        return PortAudioNull.BankRequest(0u, jarg5);
    }

    public static int CSharp_SetMedia(IntPtr jarg1, uint jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_UnsetMedia(IntPtr jarg1, uint jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_PrepareGameSyncs__SWIG_0(int jarg1, int jarg2, string jarg3, IntPtr jarg4, uint jarg5)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_PrepareGameSyncs__SWIG_1(int jarg1, int jarg2, uint jarg3, uint[] jarg4, uint jarg5)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_PrepareGameSyncs__SWIG_2(int jarg1, int jarg2, string jarg3, IntPtr jarg4, uint jarg5, IntPtr jarg6, IntPtr jarg7)
    {
        return PortAudioNull.BankRequest(0u, jarg7);
    }

    public static int CSharp_PrepareGameSyncs__SWIG_3(int jarg1, int jarg2, uint jarg3, uint[] jarg4, uint jarg5, IntPtr jarg6, IntPtr jarg7)
    {
        return PortAudioNull.BankRequest(0u, jarg7);
    }

    public static int CSharp_SetActiveListeners(ulong jarg1, uint jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetListenerSpatialization__SWIG_0(uint jarg1, bool jarg2, IntPtr jarg3, float[] jarg4)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetListenerSpatialization__SWIG_1(uint jarg1, bool jarg2, IntPtr jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetListenerPipeline(uint jarg1, bool jarg2, bool jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetRTPCValue__SWIG_0(uint jarg1, float jarg2, ulong jarg3, int jarg4, int jarg5, bool jarg6)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetRTPCValue__SWIG_1(uint jarg1, float jarg2, ulong jarg3, int jarg4, int jarg5)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetRTPCValue__SWIG_2(uint jarg1, float jarg2, ulong jarg3, int jarg4)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetRTPCValue__SWIG_3(uint jarg1, float jarg2, ulong jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetRTPCValue__SWIG_4(uint jarg1, float jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetRTPCValue__SWIG_5(string jarg1, float jarg2, ulong jarg3, int jarg4, int jarg5, bool jarg6)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetRTPCValue__SWIG_6(string jarg1, float jarg2, ulong jarg3, int jarg4, int jarg5)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetRTPCValue__SWIG_7(string jarg1, float jarg2, ulong jarg3, int jarg4)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetRTPCValue__SWIG_8(string jarg1, float jarg2, ulong jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetRTPCValue__SWIG_9(string jarg1, float jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetRTPCValueByPlayingID__SWIG_0(uint jarg1, float jarg2, uint jarg3, int jarg4, int jarg5, bool jarg6)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetRTPCValueByPlayingID__SWIG_1(uint jarg1, float jarg2, uint jarg3, int jarg4, int jarg5)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetRTPCValueByPlayingID__SWIG_2(uint jarg1, float jarg2, uint jarg3, int jarg4)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetRTPCValueByPlayingID__SWIG_3(uint jarg1, float jarg2, uint jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetRTPCValueByPlayingID__SWIG_4(string jarg1, float jarg2, uint jarg3, int jarg4, int jarg5, bool jarg6)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetRTPCValueByPlayingID__SWIG_5(string jarg1, float jarg2, uint jarg3, int jarg4, int jarg5)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetRTPCValueByPlayingID__SWIG_6(string jarg1, float jarg2, uint jarg3, int jarg4)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetRTPCValueByPlayingID__SWIG_7(string jarg1, float jarg2, uint jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ResetRTPCValue__SWIG_0(uint jarg1, ulong jarg2, int jarg3, int jarg4, bool jarg5)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ResetRTPCValue__SWIG_1(uint jarg1, ulong jarg2, int jarg3, int jarg4)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ResetRTPCValue__SWIG_2(uint jarg1, ulong jarg2, int jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ResetRTPCValue__SWIG_3(uint jarg1, ulong jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ResetRTPCValue__SWIG_4(uint jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ResetRTPCValue__SWIG_5(string jarg1, ulong jarg2, int jarg3, int jarg4, bool jarg5)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ResetRTPCValue__SWIG_6(string jarg1, ulong jarg2, int jarg3, int jarg4)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ResetRTPCValue__SWIG_7(string jarg1, ulong jarg2, int jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ResetRTPCValue__SWIG_8(string jarg1, ulong jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_ResetRTPCValue__SWIG_9(string jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetSwitch__SWIG_0(uint jarg1, uint jarg2, ulong jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetSwitch__SWIG_1(string jarg1, string jarg2, ulong jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_PostTrigger__SWIG_0(uint jarg1, ulong jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_PostTrigger__SWIG_1(string jarg1, ulong jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetState__SWIG_0(uint jarg1, uint jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetState__SWIG_1(string jarg1, string jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetGameObjectAuxSendValues(ulong jarg1, IntPtr jarg2, uint jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetGameObjectOutputBusVolume(ulong jarg1, float jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetActorMixerEffect(uint jarg1, uint jarg2, uint jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetBusEffect__SWIG_0(uint jarg1, uint jarg2, uint jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetBusEffect__SWIG_1(string jarg1, uint jarg2, uint jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetMixer__SWIG_0(uint jarg1, uint jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetMixer__SWIG_1(string jarg1, uint jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetObjectObstructionAndOcclusion(ulong jarg1, uint jarg2, float jarg3, float jarg4)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_StartOutputCapture(string jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_StopOutputCapture()
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_AddOutputCaptureMarker(string jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_StartProfilerCapture(string jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_StopProfilerCapture()
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_AddSecondaryOutput(uint jarg1, int jarg2, uint jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_RemoveSecondaryOutput(uint jarg1, int jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetSecondaryOutputVolume(uint jarg1, int jarg2, float jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_Suspend__SWIG_0(bool jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_Suspend__SWIG_1()
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_WakeupFromSuspend()
    {
        return PortAudioNull.Success;
    }

    public static void CSharp_AkMusicPlaylistCallbackInfo_playlistID_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkMusicPlaylistCallbackInfo_playlistID", jarg2);
    }

    public static uint CSharp_AkMusicPlaylistCallbackInfo_playlistID_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkMusicPlaylistCallbackInfo_playlistID");
    }

    public static void CSharp_AkMusicPlaylistCallbackInfo_uNumPlaylistItems_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkMusicPlaylistCallbackInfo_uNumPlaylistItems", jarg2);
    }

    public static uint CSharp_AkMusicPlaylistCallbackInfo_uNumPlaylistItems_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkMusicPlaylistCallbackInfo_uNumPlaylistItems");
    }

    public static void CSharp_AkMusicPlaylistCallbackInfo_uPlaylistSelection_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkMusicPlaylistCallbackInfo_uPlaylistSelection", jarg2);
    }

    public static uint CSharp_AkMusicPlaylistCallbackInfo_uPlaylistSelection_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkMusicPlaylistCallbackInfo_uPlaylistSelection");
    }

    public static void CSharp_AkMusicPlaylistCallbackInfo_uPlaylistItemDone_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkMusicPlaylistCallbackInfo_uPlaylistItemDone", jarg2);
    }

    public static uint CSharp_AkMusicPlaylistCallbackInfo_uPlaylistItemDone_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkMusicPlaylistCallbackInfo_uPlaylistItemDone");
    }

    public static IntPtr CSharp_new_AkMusicPlaylistCallbackInfo()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkMusicPlaylistCallbackInfo(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static byte CSharp_AK_INVALID_MIDI_CHANNEL_get()
    {
        return 0;
    }

    public static byte CSharp_AK_INVALID_MIDI_NOTE_get()
    {
        return 0;
    }

    public static void CSharp_AkMemSettings_uMaxNumPools_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkMemSettings_uMaxNumPools", jarg2);
    }

    public static uint CSharp_AkMemSettings_uMaxNumPools_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkMemSettings_uMaxNumPools");
    }

    public static IntPtr CSharp_new_AkMemSettings()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkMemSettings(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static void CSharp_AkMusicSettings_fStreamingLookAheadRatio_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkMusicSettings_fStreamingLookAheadRatio", jarg2);
    }

    public static float CSharp_AkMusicSettings_fStreamingLookAheadRatio_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkMusicSettings_fStreamingLookAheadRatio");
    }

    public static IntPtr CSharp_new_AkMusicSettings()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkMusicSettings(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static void CSharp_AkSegmentInfo_iCurrentPosition_set(IntPtr jarg1, int jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkSegmentInfo_iCurrentPosition", jarg2);
    }

    public static int CSharp_AkSegmentInfo_iCurrentPosition_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<int>(jarg1, "AkSegmentInfo_iCurrentPosition");
    }

    public static void CSharp_AkSegmentInfo_iPreEntryDuration_set(IntPtr jarg1, int jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkSegmentInfo_iPreEntryDuration", jarg2);
    }

    public static int CSharp_AkSegmentInfo_iPreEntryDuration_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<int>(jarg1, "AkSegmentInfo_iPreEntryDuration");
    }

    public static void CSharp_AkSegmentInfo_iActiveDuration_set(IntPtr jarg1, int jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkSegmentInfo_iActiveDuration", jarg2);
    }

    public static int CSharp_AkSegmentInfo_iActiveDuration_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<int>(jarg1, "AkSegmentInfo_iActiveDuration");
    }

    public static void CSharp_AkSegmentInfo_iPostExitDuration_set(IntPtr jarg1, int jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkSegmentInfo_iPostExitDuration", jarg2);
    }

    public static int CSharp_AkSegmentInfo_iPostExitDuration_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<int>(jarg1, "AkSegmentInfo_iPostExitDuration");
    }

    public static void CSharp_AkSegmentInfo_iRemainingLookAheadTime_set(IntPtr jarg1, int jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkSegmentInfo_iRemainingLookAheadTime", jarg2);
    }

    public static int CSharp_AkSegmentInfo_iRemainingLookAheadTime_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<int>(jarg1, "AkSegmentInfo_iRemainingLookAheadTime");
    }

    public static IntPtr CSharp_new_AkSegmentInfo()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkSegmentInfo(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static int CSharp_GetPlayingSegmentInfo__SWIG_0(uint jarg1, IntPtr jarg2, bool jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_GetPlayingSegmentInfo__SWIG_1(uint jarg1, IntPtr jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_AkCallbackSerializer_Init(IntPtr jarg1, uint jarg2)
    {
        return PortAudioNull.SerializerInit(jarg1, jarg2);
    }

    public static void CSharp_AkCallbackSerializer_Term()
    {
        PortAudioNull.SerializerTerm();
    }

    public static IntPtr CSharp_AkCallbackSerializer_Lock()
    {
        return PortAudioNull.LockCallbacks();
    }

    public static void CSharp_AkCallbackSerializer_SetLocalOutput(uint jarg1)
    {
    }

    public static void CSharp_AkCallbackSerializer_Unlock()
    {
        PortAudioNull.UnlockCallbacks();
    }

    public static IntPtr CSharp_new_AkCallbackSerializer()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkCallbackSerializer(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static int CSharp_PostCode(int jarg1, int jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_PostString__SWIG_0(string jarg1, int jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_GetTimeStamp()
    {
        return 0;
    }

    public static uint CSharp_GetNumNonZeroBits(uint jarg1)
    {
        return PortAudioNull.CountBits(jarg1);
    }

    public static uint CSharp_ResolveDialogueEvent__SWIG_0(uint jarg1, uint[] jarg2, uint jarg3, uint jarg4)
    {
        return 0u;
    }

    public static uint CSharp_ResolveDialogueEvent__SWIG_1(uint jarg1, uint[] jarg2, uint jarg3)
    {
        return 0u;
    }

    public static void CSharp_AkPositioningInfo_fCenterPct_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPositioningInfo_fCenterPct", jarg2);
    }

    public static float CSharp_AkPositioningInfo_fCenterPct_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkPositioningInfo_fCenterPct");
    }

    public static void CSharp_AkPositioningInfo_pannerType_set(IntPtr jarg1, int jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPositioningInfo_pannerType", jarg2);
    }

    public static int CSharp_AkPositioningInfo_pannerType_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<int>(jarg1, "AkPositioningInfo_pannerType");
    }

    public static void CSharp_AkPositioningInfo_posSourceType_set(IntPtr jarg1, int jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPositioningInfo_posSourceType", jarg2);
    }

    public static int CSharp_AkPositioningInfo_posSourceType_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<int>(jarg1, "AkPositioningInfo_posSourceType");
    }

    public static void CSharp_AkPositioningInfo_bUpdateEachFrame_set(IntPtr jarg1, bool jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPositioningInfo_bUpdateEachFrame", jarg2);
    }

    public static bool CSharp_AkPositioningInfo_bUpdateEachFrame_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<bool>(jarg1, "AkPositioningInfo_bUpdateEachFrame");
    }

    public static void CSharp_AkPositioningInfo_bUseSpatialization_set(IntPtr jarg1, bool jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPositioningInfo_bUseSpatialization", jarg2);
    }

    public static bool CSharp_AkPositioningInfo_bUseSpatialization_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<bool>(jarg1, "AkPositioningInfo_bUseSpatialization");
    }

    public static void CSharp_AkPositioningInfo_bUseAttenuation_set(IntPtr jarg1, bool jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPositioningInfo_bUseAttenuation", jarg2);
    }

    public static bool CSharp_AkPositioningInfo_bUseAttenuation_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<bool>(jarg1, "AkPositioningInfo_bUseAttenuation");
    }

    public static void CSharp_AkPositioningInfo_bUseConeAttenuation_set(IntPtr jarg1, bool jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPositioningInfo_bUseConeAttenuation", jarg2);
    }

    public static bool CSharp_AkPositioningInfo_bUseConeAttenuation_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<bool>(jarg1, "AkPositioningInfo_bUseConeAttenuation");
    }

    public static void CSharp_AkPositioningInfo_fInnerAngle_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPositioningInfo_fInnerAngle", jarg2);
    }

    public static float CSharp_AkPositioningInfo_fInnerAngle_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkPositioningInfo_fInnerAngle");
    }

    public static void CSharp_AkPositioningInfo_fOuterAngle_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPositioningInfo_fOuterAngle", jarg2);
    }

    public static float CSharp_AkPositioningInfo_fOuterAngle_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkPositioningInfo_fOuterAngle");
    }

    public static void CSharp_AkPositioningInfo_fConeMaxAttenuation_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPositioningInfo_fConeMaxAttenuation", jarg2);
    }

    public static float CSharp_AkPositioningInfo_fConeMaxAttenuation_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkPositioningInfo_fConeMaxAttenuation");
    }

    public static void CSharp_AkPositioningInfo_LPFCone_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPositioningInfo_LPFCone", jarg2);
    }

    public static float CSharp_AkPositioningInfo_LPFCone_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkPositioningInfo_LPFCone");
    }

    public static void CSharp_AkPositioningInfo_HPFCone_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPositioningInfo_HPFCone", jarg2);
    }

    public static float CSharp_AkPositioningInfo_HPFCone_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkPositioningInfo_HPFCone");
    }

    public static void CSharp_AkPositioningInfo_fMaxDistance_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPositioningInfo_fMaxDistance", jarg2);
    }

    public static float CSharp_AkPositioningInfo_fMaxDistance_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkPositioningInfo_fMaxDistance");
    }

    public static void CSharp_AkPositioningInfo_fVolDryAtMaxDist_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPositioningInfo_fVolDryAtMaxDist", jarg2);
    }

    public static float CSharp_AkPositioningInfo_fVolDryAtMaxDist_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkPositioningInfo_fVolDryAtMaxDist");
    }

    public static void CSharp_AkPositioningInfo_fVolAuxGameDefAtMaxDist_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPositioningInfo_fVolAuxGameDefAtMaxDist", jarg2);
    }

    public static float CSharp_AkPositioningInfo_fVolAuxGameDefAtMaxDist_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkPositioningInfo_fVolAuxGameDefAtMaxDist");
    }

    public static void CSharp_AkPositioningInfo_fVolAuxUserDefAtMaxDist_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPositioningInfo_fVolAuxUserDefAtMaxDist", jarg2);
    }

    public static float CSharp_AkPositioningInfo_fVolAuxUserDefAtMaxDist_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkPositioningInfo_fVolAuxUserDefAtMaxDist");
    }

    public static void CSharp_AkPositioningInfo_LPFValueAtMaxDist_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPositioningInfo_LPFValueAtMaxDist", jarg2);
    }

    public static float CSharp_AkPositioningInfo_LPFValueAtMaxDist_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkPositioningInfo_LPFValueAtMaxDist");
    }

    public static void CSharp_AkPositioningInfo_HPFValueAtMaxDist_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPositioningInfo_HPFValueAtMaxDist", jarg2);
    }

    public static float CSharp_AkPositioningInfo_HPFValueAtMaxDist_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkPositioningInfo_HPFValueAtMaxDist");
    }

    public static IntPtr CSharp_new_AkPositioningInfo()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkPositioningInfo(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static void CSharp_AkObjectInfo_objID_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkObjectInfo_objID", jarg2);
    }

    public static uint CSharp_AkObjectInfo_objID_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkObjectInfo_objID");
    }

    public static void CSharp_AkObjectInfo_parentID_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkObjectInfo_parentID", jarg2);
    }

    public static uint CSharp_AkObjectInfo_parentID_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkObjectInfo_parentID");
    }

    public static void CSharp_AkObjectInfo_iDepth_set(IntPtr jarg1, int jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkObjectInfo_iDepth", jarg2);
    }

    public static int CSharp_AkObjectInfo_iDepth_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<int>(jarg1, "AkObjectInfo_iDepth");
    }

    public static IntPtr CSharp_new_AkObjectInfo()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkObjectInfo(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static int CSharp_GetPosition(ulong jarg1, IntPtr jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_GetActiveListeners(ulong jarg1, out uint jarg2)
    {
        jarg2 = default(uint);
        return PortAudioNull.Success;
    }

    public static int CSharp_GetListenerPosition(uint jarg1, IntPtr jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_GetRTPCValue__SWIG_0(uint jarg1, ulong jarg2, out float jarg3, ref int jarg4)
    {
        jarg3 = default(float);
        return PortAudioNull.Success;
    }

    public static int CSharp_GetRTPCValue__SWIG_1(string jarg1, ulong jarg2, out float jarg3, ref int jarg4)
    {
        jarg3 = default(float);
        return PortAudioNull.Success;
    }

    public static int CSharp_GetSwitch__SWIG_0(uint jarg1, ulong jarg2, out uint jarg3)
    {
        jarg3 = default(uint);
        return PortAudioNull.Success;
    }

    public static int CSharp_GetSwitch__SWIG_1(string jarg1, ulong jarg2, out uint jarg3)
    {
        jarg3 = default(uint);
        return PortAudioNull.Success;
    }

    public static int CSharp_GetState__SWIG_0(uint jarg1, out uint jarg2)
    {
        jarg2 = default(uint);
        return PortAudioNull.Success;
    }

    public static int CSharp_GetState__SWIG_1(string jarg1, out uint jarg2)
    {
        jarg2 = default(uint);
        return PortAudioNull.Success;
    }

    public static int CSharp_GetGameObjectAuxSendValues(ulong jarg1, IntPtr jarg2, ref uint jarg3)
    {
        jarg3 = 0u;
        return PortAudioNull.Success;
    }

    public static int CSharp_GetGameObjectDryLevelValue(ulong jarg1, out float jarg2)
    {
        jarg2 = default(float);
        return PortAudioNull.Success;
    }

    public static int CSharp_GetObjectObstructionAndOcclusion(ulong jarg1, uint jarg2, out float jarg3, out float jarg4)
    {
        jarg3 = default(float);
        jarg4 = default(float);
        return PortAudioNull.Success;
    }

    public static int CSharp_QueryAudioObjectIDs__SWIG_0(uint jarg1, ref uint jarg2, IntPtr jarg3)
    {
        jarg2 = 0u;
        return PortAudioNull.Success;
    }

    public static int CSharp_QueryAudioObjectIDs__SWIG_1(string jarg1, ref uint jarg2, IntPtr jarg3)
    {
        jarg2 = 0u;
        return PortAudioNull.Success;
    }

    public static int CSharp_GetPositioningInfo(uint jarg1, IntPtr jarg2)
    {
        return PortAudioNull.Success;
    }

    public static bool CSharp_GetIsGameObjectActive(ulong jarg1)
    {
        return false;
    }

    public static float CSharp_GetMaxRadius(ulong jarg1)
    {
        return 0f;
    }

    public static uint CSharp_GetEventIDFromPlayingID(uint jarg1)
    {
        return PortAudioNull.EventIdFromPlayingId(jarg1);
    }

    public static ulong CSharp_GetGameObjectFromPlayingID(uint jarg1)
    {
        return 0ul;
    }

    public static int CSharp_GetPlayingIDsFromGameObject(ulong jarg1, ref uint jarg2, uint[] jarg3)
    {
        jarg2 = 0u;
        return PortAudioNull.Success;
    }

    public static int CSharp_GetCustomPropertyValue__SWIG_0(uint jarg1, uint jarg2, out int jarg3)
    {
        jarg3 = default(int);
        return PortAudioNull.Success;
    }

    public static int CSharp_GetCustomPropertyValue__SWIG_1(uint jarg1, uint jarg2, out float jarg3)
    {
        jarg3 = default(float);
        return PortAudioNull.Success;
    }

    public static void CSharp_AK_SPEAKER_SETUP_FIX_LEFT_TO_CENTER(ref uint jarg1)
    {
        jarg1 = 0u;
    }

    public static void CSharp_AK_SPEAKER_SETUP_FIX_REAR_TO_SIDE(ref uint jarg1)
    {
        jarg1 = 0u;
    }

    public static void CSharp_AK_SPEAKER_SETUP_CONVERT_TO_SUPPORTED(ref uint jarg1)
    {
        jarg1 = 0u;
    }

    public static uint CSharp_ChannelMaskToNumChannels(uint jarg1)
    {
        return PortAudioNull.CountBits(jarg1);
    }

    public static uint CSharp_ChannelMaskFromNumChannels(uint jarg1)
    {
        return PortAudioNull.MaskFromChannelCount(jarg1);
    }

    public static bool CSharp_HasSurroundChannels(uint jarg1)
    {
        return false;
    }

    public static bool CSharp_HasStrictlyOnePairOfSurroundChannels(uint jarg1)
    {
        return false;
    }

    public static bool CSharp_HasSideAndRearChannels(uint jarg1)
    {
        return false;
    }

    public static uint CSharp_BackToSideChannels(uint jarg1)
    {
        return jarg1;
    }

    public static uint CSharp_ChannelIndexToDisplayIndex(int jarg1, uint jarg2, uint jarg3)
    {
        return 0u;
    }

    public static int CSharp_AddPlayerMotionDevice__SWIG_0(byte jarg1, uint jarg2, uint jarg3, IntPtr jarg4)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_AddPlayerMotionDevice__SWIG_1(byte jarg1, uint jarg2, uint jarg3)
    {
        return PortAudioNull.Success;
    }

    public static void CSharp_RemovePlayerMotionDevice(byte jarg1, uint jarg2, uint jarg3)
    {
    }

    public static void CSharp_SetPlayerListener(byte jarg1, byte jarg2)
    {
    }

    public static void CSharp_SetPlayerVolume(byte jarg1, float jarg2)
    {
    }

    public static void CSharp_AkPlatformInitSettings_threadLEngine_set(IntPtr jarg1, IntPtr jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPlatformInitSettings_threadLEngine", jarg2);
    }

    public static IntPtr CSharp_AkPlatformInitSettings_threadLEngine_get(IntPtr jarg1)
    {
        return PortAudioNull.GetMember(jarg1, "AkPlatformInitSettings_threadLEngine");
    }

    public static void CSharp_AkPlatformInitSettings_threadBankManager_set(IntPtr jarg1, IntPtr jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPlatformInitSettings_threadBankManager", jarg2);
    }

    public static IntPtr CSharp_AkPlatformInitSettings_threadBankManager_get(IntPtr jarg1)
    {
        return PortAudioNull.GetMember(jarg1, "AkPlatformInitSettings_threadBankManager");
    }

    public static void CSharp_AkPlatformInitSettings_uLEngineDefaultPoolSize_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPlatformInitSettings_uLEngineDefaultPoolSize", jarg2);
    }

    public static uint CSharp_AkPlatformInitSettings_uLEngineDefaultPoolSize_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkPlatformInitSettings_uLEngineDefaultPoolSize");
    }

    public static void CSharp_AkPlatformInitSettings_fLEngineDefaultPoolRatioThreshold_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPlatformInitSettings_fLEngineDefaultPoolRatioThreshold", jarg2);
    }

    public static float CSharp_AkPlatformInitSettings_fLEngineDefaultPoolRatioThreshold_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkPlatformInitSettings_fLEngineDefaultPoolRatioThreshold");
    }

    public static void CSharp_AkPlatformInitSettings_uLEngineAcpBatchBufferSize_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPlatformInitSettings_uLEngineAcpBatchBufferSize", jarg2);
    }

    public static uint CSharp_AkPlatformInitSettings_uLEngineAcpBatchBufferSize_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkPlatformInitSettings_uLEngineAcpBatchBufferSize");
    }

    public static void CSharp_AkPlatformInitSettings_bHwCodecLowLatencyMode_set(IntPtr jarg1, bool jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPlatformInitSettings_bHwCodecLowLatencyMode", jarg2);
    }

    public static bool CSharp_AkPlatformInitSettings_bHwCodecLowLatencyMode_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<bool>(jarg1, "AkPlatformInitSettings_bHwCodecLowLatencyMode");
    }

    public static void CSharp_AkPlatformInitSettings_uNumRefillsInVoice_set(IntPtr jarg1, ushort jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPlatformInitSettings_uNumRefillsInVoice", jarg2);
    }

    public static ushort CSharp_AkPlatformInitSettings_uNumRefillsInVoice_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<ushort>(jarg1, "AkPlatformInitSettings_uNumRefillsInVoice");
    }

    public static void CSharp_AkPlatformInitSettings_threadMonitor_set(IntPtr jarg1, IntPtr jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkPlatformInitSettings_threadMonitor", jarg2);
    }

    public static IntPtr CSharp_AkPlatformInitSettings_threadMonitor_get(IntPtr jarg1)
    {
        return PortAudioNull.GetMember(jarg1, "AkPlatformInitSettings_threadMonitor");
    }

    public static IntPtr CSharp_new_AkPlatformInitSettings()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkPlatformInitSettings(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static int CSharp_GetPS4OutputHandle()
    {
        return 0;
    }

    public static void CSharp_AkStreamMgrSettings_uMemorySize_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkStreamMgrSettings_uMemorySize", jarg2);
    }

    public static uint CSharp_AkStreamMgrSettings_uMemorySize_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkStreamMgrSettings_uMemorySize");
    }

    public static IntPtr CSharp_new_AkStreamMgrSettings()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkStreamMgrSettings(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static void CSharp_AkDeviceSettings_pIOMemory_set(IntPtr jarg1, IntPtr jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkDeviceSettings_pIOMemory", jarg2);
    }

    public static IntPtr CSharp_AkDeviceSettings_pIOMemory_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<IntPtr>(jarg1, "AkDeviceSettings_pIOMemory");
    }

    public static void CSharp_AkDeviceSettings_uIOMemorySize_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkDeviceSettings_uIOMemorySize", jarg2);
    }

    public static uint CSharp_AkDeviceSettings_uIOMemorySize_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkDeviceSettings_uIOMemorySize");
    }

    public static void CSharp_AkDeviceSettings_uIOMemoryAlignment_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkDeviceSettings_uIOMemoryAlignment", jarg2);
    }

    public static uint CSharp_AkDeviceSettings_uIOMemoryAlignment_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkDeviceSettings_uIOMemoryAlignment");
    }

    public static void CSharp_AkDeviceSettings_ePoolAttributes_set(IntPtr jarg1, int jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkDeviceSettings_ePoolAttributes", jarg2);
    }

    public static int CSharp_AkDeviceSettings_ePoolAttributes_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<int>(jarg1, "AkDeviceSettings_ePoolAttributes");
    }

    public static void CSharp_AkDeviceSettings_uGranularity_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkDeviceSettings_uGranularity", jarg2);
    }

    public static uint CSharp_AkDeviceSettings_uGranularity_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkDeviceSettings_uGranularity");
    }

    public static void CSharp_AkDeviceSettings_uSchedulerTypeFlags_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkDeviceSettings_uSchedulerTypeFlags", jarg2);
    }

    public static uint CSharp_AkDeviceSettings_uSchedulerTypeFlags_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkDeviceSettings_uSchedulerTypeFlags");
    }

    public static void CSharp_AkDeviceSettings_threadProperties_set(IntPtr jarg1, IntPtr jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkDeviceSettings_threadProperties", jarg2);
    }

    public static IntPtr CSharp_AkDeviceSettings_threadProperties_get(IntPtr jarg1)
    {
        return PortAudioNull.GetMember(jarg1, "AkDeviceSettings_threadProperties");
    }

    public static void CSharp_AkDeviceSettings_fTargetAutoStmBufferLength_set(IntPtr jarg1, float jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkDeviceSettings_fTargetAutoStmBufferLength", jarg2);
    }

    public static float CSharp_AkDeviceSettings_fTargetAutoStmBufferLength_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<float>(jarg1, "AkDeviceSettings_fTargetAutoStmBufferLength");
    }

    public static void CSharp_AkDeviceSettings_uMaxConcurrentIO_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkDeviceSettings_uMaxConcurrentIO", jarg2);
    }

    public static uint CSharp_AkDeviceSettings_uMaxConcurrentIO_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkDeviceSettings_uMaxConcurrentIO");
    }

    public static void CSharp_AkDeviceSettings_bUseStreamCache_set(IntPtr jarg1, bool jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkDeviceSettings_bUseStreamCache", jarg2);
    }

    public static bool CSharp_AkDeviceSettings_bUseStreamCache_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<bool>(jarg1, "AkDeviceSettings_bUseStreamCache");
    }

    public static void CSharp_AkDeviceSettings_uMaxCachePinnedBytes_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkDeviceSettings_uMaxCachePinnedBytes", jarg2);
    }

    public static uint CSharp_AkDeviceSettings_uMaxCachePinnedBytes_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkDeviceSettings_uMaxCachePinnedBytes");
    }

    public static IntPtr CSharp_new_AkDeviceSettings()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkDeviceSettings(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static void CSharp_AkThreadProperties_nPriority_set(IntPtr jarg1, int jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkThreadProperties_nPriority", jarg2);
    }

    public static int CSharp_AkThreadProperties_nPriority_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<int>(jarg1, "AkThreadProperties_nPriority");
    }

    public static void CSharp_AkThreadProperties_dwAffinityMask_set(IntPtr jarg1, ulong jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkThreadProperties_dwAffinityMask", jarg2);
    }

    public static ulong CSharp_AkThreadProperties_dwAffinityMask_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<ulong>(jarg1, "AkThreadProperties_dwAffinityMask");
    }

    public static void CSharp_AkThreadProperties_uStackSize_set(IntPtr jarg1, uint jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkThreadProperties_uStackSize", jarg2);
    }

    public static uint CSharp_AkThreadProperties_uStackSize_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<uint>(jarg1, "AkThreadProperties_uStackSize");
    }

    public static void CSharp_AkThreadProperties_uSchedPolicy_set(IntPtr jarg1, int jarg2)
    {
        PortAudioNull.SetField(jarg1, "AkThreadProperties_uSchedPolicy", jarg2);
    }

    public static int CSharp_AkThreadProperties_uSchedPolicy_get(IntPtr jarg1)
    {
        return PortAudioNull.GetField<int>(jarg1, "AkThreadProperties_uSchedPolicy");
    }

    public static IntPtr CSharp_new_AkThreadProperties()
    {
        return PortAudioNull.NewHandle();
    }

    public static void CSharp_delete_AkThreadProperties(IntPtr jarg1)
    {
        PortAudioNull.Release(jarg1);
    }

    public static void CSharp_Term()
    {
        PortAudioNull.Term();
    }

    public static int CSharp_Init(IntPtr jarg1, IntPtr jarg2, IntPtr jarg3, IntPtr jarg4, IntPtr jarg5, IntPtr jarg6)
    {
        return PortAudioNull.Init();
    }

    public static void CSharp_GetDefaultStreamSettings(IntPtr jarg1)
    {
    }

    public static void CSharp_GetDefaultDeviceSettings(IntPtr jarg1)
    {
    }

    public static void CSharp_GetDefaultMusicSettings(IntPtr jarg1)
    {
    }

    public static void CSharp_GetDefaultInitSettings(IntPtr jarg1)
    {
    }

    public static void CSharp_GetDefaultPlatformInitSettings(IntPtr jarg1)
    {
    }

    public static uint CSharp_GetMajorMinorVersion()
    {
        return (2016u << 16) | 1u;
    }

    public static uint CSharp_GetSubminorBuildVersion()
    {
        return 0u;
    }

    public static int CSharp_RegisterGameObjInternal(int jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_RegisterGameObjInternal_WithMask(int jarg1, uint jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_RegisterGameObjInternal_WithName(int jarg1, string jarg2)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_RegisterGameObjInternal_WithName_WithMask(int jarg1, string jarg2, uint jarg3)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetBasePath(string jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetCurrentLanguage(string jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_LoadFilePackage(string jarg1, out uint jarg2, int jarg3, bool jarg4)
    {
        jarg2 = default(uint);
        return PortAudioNull.Success;
    }

    public static int CSharp_AddBasePath(string jarg1)
    {
        return PortAudioNull.Success;
    }

    public static uint CSharp_SetGameName(string jarg1)
    {
        return 0u;
    }

    public static int CSharp_UnloadFilePackage(uint jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_UnloadAllFilePackages()
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_UnregisterGameObjInternal(int jarg1)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetObjectPosition(ulong jarg1, float jarg2, float jarg3, float jarg4, float jarg5, float jarg6, float jarg7)
    {
        return PortAudioNull.Success;
    }

    public static int CSharp_SetListenerPosition(float jarg1, float jarg2, float jarg3, float jarg4, float jarg5, float jarg6, float jarg7, float jarg8, float jarg9, uint jarg10)
    {
        return PortAudioNull.Success;
    }

    public static bool CSharp_IsInitialized()
    {
        return PortAudioNull.IsInitialized;
    }

    public static IntPtr CSharp_WwiseObjectID_SWIGUpcast(IntPtr jarg1)
    {
        return jarg1;
    }

    public static IntPtr CSharp_Playlist_SWIGUpcast(IntPtr jarg1)
    {
        return jarg1;
    }
}
