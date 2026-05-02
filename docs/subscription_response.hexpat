#pragma endian big

import std.core;

u32 recordStart;
u32 recordEnd;
u32 stringStart;
u32 stringEnd;
u32 extraZeros;

struct RecordLengthTable {
  u16 unknown;
  u16 len_1;
  u8 len_2;
  u8 len_3;
  u16 len_4;
  u8 len_5;
  u8 len_6;
  u16 len_7;
  u16 len_8;
  u16 len_9;
};

struct SubscriptionData {
  u8 len_1;
  u8 len_2;
  u16 channel_number;
  u32 constant_2;
  u16 channel_number_2;
  u32 pad_1;
  u16 constant_3;
  u32 pad_2;
  u16 name_1_index;
  u16 weird_lookup_table_index;
  RecordLengthTable table @ weird_lookup_table_index;
  u32 pad_3;
  if (len_1 == 0x16) {
    u32 bruh;
  }
  u16 pad_4;
  u16 name_2_index;
  u64 pad_5;
  u16 pad_6;
  u16 pad_7;
  u16 device_id_index;
  u16 device_name_index;
  u16 subscriptionStatus;
  u8 totalConnections;
  u8 activeConnections;
  u8 unknown_1;
  u8 unknown_2;
  u8 unknown_3;
  u8 unknown_4;
  
  char default_name[] @ name_2_index;
  char custom_name[] @ name_1_index;
  if (device_name_index != 0) {
    char tx_device_name[] @ device_name_index;
  }
  if (device_id_index != 0) {
    char tx_device_channel[] @ device_id_index;
  }
  
  
};

struct SectionData {
  SubscriptionData data @ parent.offsets[std::core::array_index()];
};

struct SubscriptionResponse {
  u16 proto;
  u16 length;
  u16 seqNum;
  u16 opCode;
  u8 pad_1[8];
  u8 count_1;
  u8 count_2;
  u16 offsets[count_2];
  SectionData record[count_2];
};

SubscriptionResponse response @ 0x0;