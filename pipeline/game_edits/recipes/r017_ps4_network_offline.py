"""017-ps4-network-offline."""
from pipeline.game_edits import Delete, File, Lines, Recipe  # noqa: F401

STEPS = [
    Delete([
        "Assets/Scripts/Assembly-CSharp/PS4SceNetCtl.cs",
        "Assets/Scripts/Assembly-CSharp/Wish/Core/PS4SceNetCtlAp.cs",
        "Assets/Scripts/Assembly-CSharp/Wish/Core/otg_peeper.cs",
    ]),
    File("Assets/Scripts/Assembly-CSharp/PS4Matching2.cs", [
        Lines(977, 2, "7d172943981e0369", (
            '\tpublic static int SignalingGetPeerNetInfoResult(ushort ctxId, uint reqId, byte[] netinfo)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(974, 2, "ffd1b51ab5e0ead6", (
            '\tpublic static int SignalingGetLocalNetInfo(byte[] netinfo)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(971, 2, "f6f22f3c318345f1", (
            '\tpublic static int SignalingGetConnectionStatus_IPAndPort(ushort ctxId, ulong roomId, ushort memberId, IntPtr outConnectionInfo)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(968, 2, "9713ecce2e9b4449", (
            '\tpublic static int SignalingGetConnectionInfo_IPAndPort(ushort ctxId, ulong roomId, ushort memberId, IntPtr outConnectionInfo)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(965, 2, "c3beab4194ca0d22", (
            '\tpublic static int SignalingGetConnectionStatus(ushort ctxId, ulong roomId, ushort memberId, byte[] connStatus, byte[] peerAddr, byte[] peerPort)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(962, 2, "7b3c198559848413", (
            '\tpublic static int SignalingGetConnectionInfo_uint32(ushort ctxId, ulong roomId, ushort memberId, int code, ref uint stat)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(959, 2, "b8282093b3c805ae", (
            '\tpublic static int SignalingGetConnectionInfo(ushort ctxId, ulong roomId, ushort memberId, int code, byte[] connInfo)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(956, 2, "8abbb8b1ad9f8fd8", (
            '\tpublic static int SignalingCancelPeerNetInfo(ushort ctxId, uint reqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(953, 2, "706248c22e5f5055", (
            '\tpublic static int SignalingGetPingInfo(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(950, 2, "47e74bc0690ed112", (
            '\tpublic static int SignalingGetPeerNetInfo(ushort ctxId, ulong roomId, ushort roomMemberId, byte[] reqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(947, 2, "d22f7bba21325b1a", (
            '\tpublic static int SetUserInfo(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(944, 2, "e98b07c84de5c7e8", (
            '\tpublic static int SetSignalingOptParam(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(941, 2, "69acfb954788a047", (
            '\tpublic static int SetRoomMemberDataInternal(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(938, 2, "bdb6953bd70ace34", (
            '\tpublic static int SetRoomDataInternal(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(935, 2, "384601c441895a07", (
            '\tpublic static int SetRoomDataExternal(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(932, 2, "e5b45105bae31a35", (
            '\tpublic static int SetLobbyMemberDataInternal(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(929, 2, "ba06eacf8c49dedf", (
            '\tpublic static int SendRoomMessage(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(926, 2, "251362085a4f435c", (
            '\tpublic static int SendRoomChatMessage(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(923, 2, "168a9ec32e60ffd9", (
            '\tpublic static int SendLobbyChatMessage(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(920, 2, "a80262171a4aa265", (
            '\tpublic static int SearchRoom(ushort ctxId, IntPtr reqParam, IntPtr optParam, ref uint assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(917, 2, "7324465488643b08", (
            '\tpublic static int LeaveRoom(ushort ctxId, IntPtr reqParam, IntPtr optParam, ref uint assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(914, 2, "1f5966e8c0892bb2", (
            '\tpublic static int LeaveLobby(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(911, 2, "598da78cfad4953e", (
            '\tpublic static int KickoutRoomMember(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(908, 2, "06d7b08bc6daa116", (
            '\tpublic static int JoinRoom(ushort ctxId, IntPtr reqParam, IntPtr optParam, ref uint assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(905, 2, "016a883479700144", (
            '\tpublic static int JoinLobby(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(902, 2, "1ce9b7559779c14c", (
            '\tpublic static int GrantRoomOwner(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(898, 3, "c7950b3ed09f8acc", (
            '\tpublic static bool RequestErr_ClearLostFlag()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(894, 3, "37b1761e671eb340", (
            '\tpublic static bool RequestErr_GetLostFlag()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(890, 3, "9d0881bd3616b4ab", (
            '\tpublic static bool RequestErr_Get(IntPtr outItem)\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(886, 3, "4d7fcfecbcbb212f", (
            '\tpublic static bool RequestErr_GetAreAny()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(882, 3, "0b8da0db2bb21a2d", (
            '\tpublic static bool LeaveRoomResponse_ClearLostFlag()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(878, 3, "24054eed22f15368", (
            '\tpublic static bool LeaveRoomResponse_GetLostFlag()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(874, 3, "0308861bd3ecf30d", (
            '\tpublic static bool LeaveRoomResponse_Get(IntPtr outItem)\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(870, 3, "4f73263c34c204bf", (
            '\tpublic static bool LeaveRoomResponse_GetAreAny()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(867, 2, "de6e95554599eb66", (
            '\tpublic static void ClearRoomSearchCompleteFlag()\n'
            '\t{\n'
            '\t}\n'
        )),
        Lines(863, 3, "ec7414c635b9a2de", (
            '\tpublic static bool GetRoomSearchCompleteFlag()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(859, 3, "6389da3001855cbd", (
            '\tpublic static bool RoomSearchResult_ClearLostFlag()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(855, 3, "a3b9927070130062", (
            '\tpublic static bool RoomSearchResult_GetLostFlag()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(851, 3, "1f05d439c567b599", (
            '\tpublic static bool RoomSearchResult_Get(IntPtr outItem)\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(847, 3, "d930576d4f4ff8bb", (
            '\tpublic static bool RoomSearchResult_GetAreAny()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(843, 3, "d7fe9ee262e77371", (
            '\tpublic static bool JoinedRoom_Data_ClearLostFlag()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(839, 3, "e21a6091467d3baf", (
            '\tpublic static bool JoinedRoom_Data_GetLostFlag()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(835, 3, "a4a16615d7fb6c3e", (
            '\tpublic static bool JoinedRoom_Data_Get(IntPtr outItem)\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(831, 3, "0cb566acab5dc4f7", (
            '\tpublic static bool JoinedRoom_Data_GetAreAny()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(827, 3, "b970c9f225519aa4", (
            '\tpublic static bool CreateJoinedRoom_Data_ClearLostFlag()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(823, 3, "2776da7e211a1ddd", (
            '\tpublic static bool CreateJoinedRoom_Data_GetLostFlag()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(819, 3, "11c558482ae34732", (
            '\tpublic static bool CreateJoinedRoom_Data_Get(IntPtr outItem)\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(815, 3, "38aca673ba7e9965", (
            '\tpublic static bool CreateJoinedRoom_Data_GetAreAny()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(811, 3, "c4473d11a33fa45d", (
            '\tpublic static bool World_Data_ClearLostFlag()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(807, 3, "d43301c51c9735f4", (
            '\tpublic static bool World_Data_GetLostFlag()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(803, 3, "988a6506a0d5e3af", (
            '\tpublic static bool World_Data_Get(IntPtr outItem)\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(799, 3, "baa2add791a8e1e8", (
            '\tpublic static bool World_Data_GetAreAny()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(796, 2, "688d6c58acd306e8", (
            '\tpublic static int GetWorldInfoList(ushort ctxId, IntPtr reqParam, IntPtr optParam, ref uint assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(793, 2, "c69f3178619c6863", (
            '\tpublic static int GetUserInfoList(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(790, 2, "2bd1c9c5e5ac3022", (
            '\tpublic static int GetRoomMemberDataInternal(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(787, 2, "6b780299067b8be5", (
            '\tpublic static int GetRoomMemberDataExternalList(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(784, 2, "d5a43cda2d9cef02", (
            '\tpublic static int GetRoomDataInternal(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(781, 2, "09ea9cfdc2fdab88", (
            '\tpublic static int GetRoomDataExternalList(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(778, 2, "ed026687e054a80b", (
            '\tpublic static int GetLobbyMemberDataInternalList(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(775, 2, "3cbf7e2e68eeec2b", (
            '\tpublic static int GetLobbyMemberDataInternal(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(772, 2, "bae50632748bf83f", (
            '\tpublic static int GetLobbyInfoList(ushort ctxId, byte[] reqParam, byte[] optParam, byte[] assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(769, 2, "522b8bca3eeee9e6", (
            '\tpublic static int CreateJoinRoom(ushort ctxId, IntPtr reqParam, IntPtr optParam, ref uint assignedReqId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(766, 2, "e73b14952d6710b2", (
            '\tpublic static int GetSignalingOptParamLocal(ushort ctxId, ulong roomId, byte[] signalingOptParam)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(763, 2, "329a512c75ce1b2b", (
            '\tpublic static int GetServerId(ushort ctxId, ref ushort serverId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(760, 2, "a6e55df42ce12aeb", (
            '\tpublic static int GetRoomPasswordLocal(ushort ctxId, ulong roomId, byte[] withPassword, byte[] roomPassword)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(757, 2, "e49dc7bb86c2b220", (
            '\tpublic static int GetRoomMemberIdListLocal(ushort ctxId, ulong roomId, int sortMethod, byte[] memberId, ulong memberIdNum)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(754, 2, "217c07b737a50d67", (
            '\tpublic static void SignalingEventData_ClearLostFlag()\n'
            '\t{\n'
            '\t}\n'
        )),
        Lines(750, 3, "0801fe8d9d336121", (
            '\tpublic static bool SignalingEventData_GetLostFlag()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(746, 3, "71ddd438cc9fdd9d", (
            '\tpublic static bool SignalingEventData_Get(IntPtr outItem)\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(742, 3, "9b2147022ead8002", (
            '\tpublic static bool SignalingEventData_GetAreAny()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(739, 2, "1f18a3f105239ea9", (
            '\tpublic static int RegisterSignalingCallback(ushort ctxId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(735, 3, "2bce5e1cbeffa2a2", (
            '\tpublic static bool RoomEvent_RoomMemberUpdateInfo_Data_ClearLostFlag()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(731, 3, "5baeb430d4fca999", (
            '\tpublic static bool RoomEvent_RoomMemberUpdateInfo_Data_GetLostFlag()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(727, 3, "c68ba17503eab9ac", (
            '\tpublic static bool RoomEvent_RoomMemberUpdateInfo_Data_Get(IntPtr outItem)\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(723, 3, "bf178f03a8b31ffe", (
            '\tpublic static bool RoomEvent_RoomMemberUpdateInfo_Data_GetAreAny()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(720, 2, "b9a743d633f74422", (
            '\tpublic static void RoomEvent_UpdateInfo_Data_ClearLostFlag()\n'
            '\t{\n'
            '\t}\n'
        )),
        Lines(716, 3, "daee5286eaae892c", (
            '\tpublic static bool RoomEvent_UpdateInfo_Data_GetLostFlag()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(712, 3, "d723fdec075d2414", (
            '\tpublic static bool RoomEvent_UpdateInfo_Data_Get(IntPtr outItem)\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(708, 3, "bafcbad48f3353db", (
            '\tpublic static bool RoomEvent_UpdateInfo_Data_GetAreAny()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(705, 2, "5c670d60f650addb", (
            '\tpublic static void RoomEvent_Data_ClearLostFlag()\n'
            '\t{\n'
            '\t}\n'
        )),
        Lines(701, 3, "b747c8805206af86", (
            '\tpublic static bool RoomEvent_Data_GetLostFlag()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(697, 3, "24aa09358b2d2bb0", (
            '\tpublic static bool RoomEvent_Data_Get(IntPtr outItem)\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(693, 3, "0b67628f2bd41f3a", (
            '\tpublic static bool RoomEvent_Data_GetAreAny()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(690, 2, "eeb4275be73d923d", (
            '\tpublic static int RegisterRoomEventCallback()\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(687, 2, "ddab77b8caacdc4a", (
            '\tpublic static void ContextCallbackData_ClearLostFlag()\n'
            '\t{\n'
            '\t}\n'
        )),
        Lines(683, 3, "40a87b43084e1a9c", (
            '\tpublic static bool ContextCallbackData_GetLostFlag()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(679, 3, "23e442a9e18d446b", (
            '\tpublic static bool ContextCallbackData_Get(IntPtr outItem)\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(675, 3, "645f0181f2317597", (
            '\tpublic static bool ContextCallbackData_GetAreAny()\n'
            '\t{\n'
            '\t\treturn false;\n'
            '\t}\n'
        )),
        Lines(672, 2, "f8b7f3c38fbccb76", (
            '\tpublic static int RegisterContextCallback()\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(669, 2, "de9ea2fff4ff0b11", (
            '\tpublic static int DestroyContext(ushort ctxId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(666, 2, "ba7bbaee98e9e953", (
            '\tpublic static int CreateContext(int serviceLabel, ref ushort ctxId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(663, 2, "8008754eb481408b", (
            '\tpublic static int ContextStop(ushort ctxId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(660, 2, "75cb23cf6132ad03", (
            '\tpublic static int ContextStart(ushort ctxId, ulong timeout)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(657, 2, "b1a63d21e152be57", (
            '\tpublic static int AbortContextStart(ushort ctxId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(654, 2, "3576691e3540badf", (
            '\tpublic static int Terminate()\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(651, 2, "66c85c16fd7a9e18", (
            '\tpublic static int Initialize(IntPtr param)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(648, 2, "7b0d6e020eba6906", (
            '\tpublic static int UnloadModule()\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(645, 2, "07c598e3dab1fb6e", (
            '\tpublic static int LoadModule()\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
    ]),
    File("Assets/Scripts/Assembly-CSharp/PS4Net.cs", [
        Lines(291, 2, "3d0eca21bd4bda24", (
            '\tpublic static int EtherNtostr(IntPtr n, StringBuilder sb, int len)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(288, 2, "dc917c56daec1016", (
            '\tpublic static int GetMacAddress(IntPtr addr, int flags)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(285, 2, "14e8f0e17cc99ae2", (
            '\tpublic static int Setsockopt(int s, int level, int optname, byte[] optval, int optlen)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(282, 2, "5a7dfdbd4c598a02", (
            '\tpublic static int EmulationGet(IntPtr param, int flags)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(279, 2, "ff1ddb001f5de7ce", (
            '\tpublic static int EmulationSet(IntPtr param, int flags)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(276, 2, "908bb0aa84eaec6e", (
            '\tpublic static int SocketClose(int sock)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(273, 2, "eeb5b84f4fb8307b", (
            '\tpublic static int SocketBind_Any(int sock, int family, uint port, uint vport)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(270, 2, "8801febb32437052", (
            '\tpublic static int SocketBind(int sock, int family, string host, uint port, uint vport)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(267, 2, "558ad3c83709ab24", (
            '\tpublic static int CreateSocket(string name, int family, int type, int protocol)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
    ]),
    File("Assets/Scripts/Assembly-CSharp/PS4NetDump.cs", [
        Lines(91, 2, "3a846953b358ba3f", (
            '\tprivate static int Abort(int logID)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(88, 2, "0bba5bf88117751a", (
            '\tprivate static int Destroy(int logID)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(85, 2, "b5522c7d98d8daa4", (
            '\tprivate static int Read(int logID, byte[] buffer, int bufferLen)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(82, 2, "db89c57640a1b61c", (
            '\tprivate static int CreateEthernet(string name, int len)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(79, 2, "c0c54e4ab34ec399", (
            '\tprivate static int CreateWifi(string name, int len)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
    ]),
    File("Assets/Scripts/Assembly-CSharp/PS4Rudp.cs", [
        Lines(292, 2, "77c3ac728a04be70", (
            '\tpublic static int PollWait(int pollId, IntPtr events, int eventSizeBytes, int timeout)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(289, 2, "ee1dca832b48280a", (
            '\tpublic static int PollDestroy(int pollId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(286, 2, "8b5a79c543bb40ed", (
            '\tpublic static int PollCreate(int size)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(283, 2, "2f15ade7f4d487cb", (
            '\tpublic static int PollControl(int pollId, int op, int ctxId, uint events)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(280, 2, "09b64b00ea65a6c4", (
            '\tpublic static int PollCancel(int pollId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(277, 2, "5f9a0ac7a39ef889", (
            '\tpublic static int Write(int ctxId, byte[] data, int len, uint flags)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(274, 2, "f54265f050bcaf71", (
            '\tpublic static int Read(int ctxId, byte[] data, int len, uint flags)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(271, 2, "33319d849f68dfb0", (
            '\tpublic static int GetSizeWritable(int ctxId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(268, 2, "5c416104b62579c3", (
            '\tpublic static int GetSizeReadable(int ctxId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(265, 2, "0c95a835f6f39d1b", (
            '\tpublic static int GetNumberOfPacketsToRead(int ctxId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(262, 2, "8bb2d6a26e579d8b", (
            '\tpublic static int NetFlush()\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(259, 2, "a4e63ce7d9b03388", (
            '\tpublic static int Flush(int ctxId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(256, 2, "bb9e2c95dadaf5a5", (
            '\tpublic static int Terminate(int ctxId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(253, 2, "1a32fb3547439127", (
            '\tpublic static int Initiate(int family, string ip, uint port, int ctxId, uint vport)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(250, 2, "f9119d680b5182ce", (
            '\tpublic static int Bind(int ctxId, int soc, uint vport, uint muxMode)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(247, 2, "0b5a068672a6579e", (
            '\tpublic static int GetStatus(IntPtr status, int statusSize)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(244, 2, "d38dcb0dfbcd18bd", (
            '\tpublic static int GetContextStatus(int ctxId, IntPtr status, int statusSize)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(241, 2, "f88617f6f28af851", (
            '\tpublic static int GetContextStatus_state(int ctxId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(238, 2, "2b46bef2ee3653a4", (
            '\tpublic static int SetOption(int ctxId, int option, byte[] optVal, int optLen)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(235, 2, "4cf40681a209d6e5", (
            '\tpublic static int GetOption(int ctxId, int option, byte[] optVal, int optLen)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(232, 2, "122d2f66d49e78ba", (
            '\tpublic static int CreateContext()\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(229, 2, "1c02a71528e7ec54", (
            '\tpublic static int SetMaxSegmentSize(uint mss)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(226, 2, "373c6681a6b9260e", (
            '\tpublic static int Init(byte[] memPool, int memPoolSize)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(223, 2, "d6dab6c42261143c", (
            '\tpublic static int GetMaxSegmentSize()\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(220, 2, "db57dc0cd01a55fe", (
            '\tpublic static int SetEventHandler()\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(217, 2, "1246c4e8079f7cf7", (
            '\tpublic static int End()\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(214, 2, "20b5fff5aeb0c46e", (
            '\tpublic static int EnableInternalIOThread(uint stackSize, uint priority)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(211, 2, "10ae1fe549d322b0", (
            '\tpublic static int UnloadModule()\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(208, 2, "6fc60e2f8471ab8a", (
            '\tpublic static int LoadModule()\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
    ]),
    File("Assets/Scripts/Assembly-CSharp/PS4WebAPI.cs", [
        Lines(309, 2, "95ce6930ff1e83c0", (
            '\tprivate static int GetHttpMemoryPoolStats(IntPtr stats)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(297, 2, "e76ae455f8e19224", (
            '\tprivate static int GetInvitationExpiredFlag(int idx)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(285, 2, "34ae7095ba268f53", (
            '\tprivate static IntPtr GetInvitationUpdateDate(int idx)\n'
            '\t{\n'
            '\t\treturn IntPtr.Zero;\n'
            '\t}\n'
        )),
        Lines(273, 2, "09386794484b41e4", (
            '\tprivate static IntPtr GetInvitationReceivedDate(int idx)\n'
            '\t{\n'
            '\t\treturn IntPtr.Zero;\n'
            '\t}\n'
        )),
        Lines(261, 2, "e2d73390fbab3eeb", (
            '\tprivate static IntPtr GetInvitationSessionId(int idx)\n'
            '\t{\n'
            '\t\treturn IntPtr.Zero;\n'
            '\t}\n'
        )),
        Lines(249, 2, "0c265f661de52750", (
            '\tprivate static IntPtr GetInvitationOnlineId(int idx)\n'
            '\t{\n'
            '\t\treturn IntPtr.Zero;\n'
            '\t}\n'
        )),
        Lines(237, 2, "281d5f8ae910b504", (
            '\tprivate static int GetInvitationUsedFlag(int idx)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(225, 2, "a61d11d33ab69d4c", (
            '\tprivate static IntPtr GetInvitationID(int idx)\n'
            '\t{\n'
            '\t\treturn IntPtr.Zero;\n'
            '\t}\n'
        )),
        Lines(213, 2, "500dc912d3eea019", (
            '\tprivate static int GetNumInvitations()\n'
            '\t{\n'
            '\t\treturn 0;\n'
            '\t}\n'
        )),
        Lines(201, 2, "5672619c555df647", (
            '\tprivate static int GetInvitations()\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(189, 2, "7a22de39fae9b9d3", (
            '\tprivate static int UpdateGameSessionData(string sessionID, string sessionPrivacy, string sessionName, string sessionStatus, int sessionMaxUser, string sessionLockFlag)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(177, 2, "3880637e7a614efb", (
            '\tprivate static int NpWebApiGetSession(string sessionID, IntPtr sessionData)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(165, 2, "bd2ec090ac7df88e", (
            '\tprivate static int GetInitialUserPSNAccount(StringBuilder psnID)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(153, 2, "bde03bbf79cc917d", (
            '\tprivate static int InviteToSession(string sessionID, string invitationMessage, string[] psnIDs, int psnIDsSize)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(141, 2, "f2fcd4d6369a1a56", (
            '\tprivate static int JoinGameSession(string sessionID)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(129, 2, "b74aff1bcc4cd00c", (
            '\tprivate static int LeaveGameSession(string sessionID)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(117, 2, "f3ee86069b5feb88", (
            '\tprivate static int CreateGameSession(StringBuilder sessionID, int playerCount, ulong roomID, string sessionName, string sessionStatus, string sessionPrivacy, string sessionLockFlag, byte[] imageBuffer, int imageBufferSize)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(105, 2, "d5e9721eb8fa784f", (
            '\tprivate static int Initialize()\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(93, 2, "888c6299d5758589", (
            '\tprivate static int OnUserServiceLogout(int sceUserServiceUserId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(81, 2, "ad7d0f63d6f3b509", (
            '\tprivate static int OnUserServiceLogin(int sceUserServiceUserId)\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
        Lines(69, 2, "4d93edbad33c8a6e", (
            '\tprivate static int RefreshUserWebAPIContext()\n'
            '\t{\n'
            '\t\treturn PS4NetworkOffline.ErrorNotAvailable;\n'
            '\t}\n'
        )),
    ]),
]

RECIPE = Recipe("017-ps4-network-offline", STEPS)
