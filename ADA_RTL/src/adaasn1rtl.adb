
package body adaasn1rtl with Spark_Mode is

   MASKS  : constant OctetBuffer_0_7 := OctetBuffer_0_7'(0 => 16#80#, 1=> 16#40#, 2=>16#20#, 3=>16#10#, 4=>16#08#, 5=>16#04#, 6=>16#02#, 7=>16#01#);
   MASKSB : constant OctetBuffer_0_7 := OctetBuffer_0_7'(0 => 16#00#, 1=> 16#01#, 2=>16#03#, 3=>16#07#, 4=>16#1F#, 5=>16#3F#, 6=>16#7F#, 7=>16#FF#);
   
   
   function To_UInt (IntVal : Asn1Int) return Asn1UInt is
      ret : Asn1UInt;
   begin
      if IntVal < 0 then
         ret := Asn1UInt (-(IntVal + 1));
         ret := not ret;
      else
         ret := Asn1UInt (IntVal);
      end if;
      return ret;
   end To_UInt;
   
   
   function Sub (A : in Asn1Int; B : in Asn1Int) return Asn1UInt
   is
      ret : Asn1UInt;
      au  : Asn1UInt;
      bu  : Asn1UInt;
   begin

      au := To_UInt (A);
      bu := To_UInt (B);

      if au >= bu then
         ret := au - bu;
      else
         ret := bu - au;
         ret := not ret;
         ret := ret + 1;
      end if;

      return ret;
   end Sub;
   
   function GetBytes (V : Asn1UInt) return Asn1Byte
   is
      Ret : Asn1Byte;
   begin
      if V < 16#100# then
         Ret := 1;
      elsif V < 16#10000# then
         Ret := 2;
      elsif V < 16#1000000# then
         Ret := 3;
      elsif V < 16#100000000# then
         Ret := 4;
      elsif V < 16#10000000000# then
         Ret := 5;
      elsif V < 16#1000000000000# then
         Ret := 6;
      elsif V < 16#100000000000000# then
         Ret := 7;
      else
         Ret := 8;
      end if;
      return Ret;
   end GetBytes;
   
   
   
   function BitStream_init (Bitstream_Size_In_Bytes : Positive) return Bitstream
   is
      (Bitstream'(Size_In_Bytes    => Bitstream_Size_In_Bytes,
                 Current_Bit_Pos  => 0,
                 Buffer           => (others => 0)));
   
   
   
   procedure BitStream_AppendBit(bs : in out BitStream; Bit_Value : in BIT) 
   is
      Current_Byte : constant Integer   := bs.Buffer'First + bs.Current_Bit_Pos / 8;
      Current_Bit  : constant BIT_RANGE := bs.Current_Bit_Pos mod 8;
   begin 

         bs.buffer(Current_Byte) := 
           (if Bit_Value = 1 then
               bs.buffer(Current_Byte) or MASKS(Current_Bit)
            else 
               bs.buffer(Current_Byte) and (not MASKS(Current_Bit)));
      
      bs.Current_Bit_Pos := bs.Current_Bit_Pos + 1;   

      end;
   
   procedure BitStream_ReadBit(bs : in out BitStream; Bit_Value : out BIT; result :    out Boolean)
   is
      Current_Byte : constant Integer   := bs.Buffer'First + bs.Current_Bit_Pos / 8;
      Current_Bit  : constant BIT_RANGE := bs.Current_Bit_Pos mod 8;
   begin
      result := bs.Current_Bit_Pos < Natural'Last and bs.Current_Bit_Pos < bs.Size_In_Bytes * 8;

      if (bs.buffer(Current_Byte) and MASKS(Current_Bit)) > 0 then
         Bit_Value := 1;
      else
         Bit_Value := 0;
      end if;
      
      bs.Current_Bit_Pos := bs.Current_Bit_Pos + 1;   
   end;
   
   
   procedure BitStream_AppendByte(bs : in out BitStream; Byte_Value : in Asn1Byte; Negate : in Boolean)
   is
      Current_Byte : constant Integer   := bs.Buffer'First + bs.Current_Bit_Pos / 8;
      Current_Bit  : constant BIT_RANGE := bs.Current_Bit_Pos mod 8;
      byteVal : Asn1Byte;
      ncb :BIT_RANGE;
   begin
      if Negate then
         byteVal := not Byte_Value;
      else
         byteVal := Byte_Value;
      end if;
      
      if Current_Bit > 0 then
         ncb := 8 - Current_Bit;
         bs.buffer(Current_Byte) := bs.buffer(Current_Byte) or Shift_right(ByteVal, Current_Bit);
         bs.buffer(Current_Byte+1) := Shift_left(ByteVal, ncb);
      else
         bs.buffer(Current_Byte) := ByteVal;
      end if;
       bs.Current_Bit_Pos := bs.Current_Bit_Pos + 8;
   end;
   
   
   procedure BitStream_DecodeByte(bs : in out BitStream; Byte_Value : out Asn1Byte; success   :    out Boolean) 
   is
      Current_Byte : constant Integer   := bs.Buffer'First + bs.Current_Bit_Pos / 8;
      Current_Bit  : constant BIT_RANGE := bs.Current_Bit_Pos mod 8;
      ncb :BIT_RANGE;
   begin
      success := bs.Current_Bit_Pos < Natural'Last - 8 and then  
                bs.Size_In_Bytes < Positive'Last/8 and  then
        bs.Current_Bit_Pos < bs.Size_In_Bytes * 8 - 8;
      
      if Current_Bit > 0 then
         ncb := 8 - Current_Bit;
         Byte_Value := Shift_left(bs.buffer(Current_Byte), Current_Bit);
         Byte_Value := Byte_Value or Shift_right(bs.buffer(Current_Byte + 1), ncb);
      else
         Byte_Value := bs.buffer(Current_Byte);
      end if;

      bs.Current_Bit_Pos := bs.Current_Bit_Pos + 8;
      
   end;
   
   procedure BitStream_ReadNibble(bs : in out BitStream; Byte_Value : out Asn1Byte; success   :    out Boolean)
   is
      Current_Byte : constant Integer   := bs.Buffer'First + bs.Current_Bit_Pos / 8;
      Current_Bit  : constant BIT_RANGE := bs.Current_Bit_Pos mod 8;
      totalBitsForNextByte : BIT_RANGE;
   begin
      success := bs.Current_Bit_Pos < Natural'Last - 4 and then  
                bs.Size_In_Bytes < Positive'Last/8 and  then
        bs.Current_Bit_Pos < bs.Size_In_Bytes * 8 - 4;

      if Current_Bit < 4 then
         Byte_Value := Shift_right(bs.buffer(Current_Byte), 4 - Current_Bit) and 16#0F#;
      else
         totalBitsForNextByte := Current_Bit - 4;
         Byte_Value := Shift_left(bs.buffer(Current_Byte), totalBitsForNextByte);
         --bs.currentBytePos := bs.currentBytePos + 1;
         Byte_Value := Byte_Value or (Shift_right(bs.buffer(Current_Byte + 1), 8 - totalBitsForNextByte));
         Byte_Value := Byte_Value and 16#0F#;
      end if;
      bs.Current_Bit_Pos := bs.Current_Bit_Pos + 4;
   end;
   
   
   procedure BitStream_AppendPartialByte(bs : in out BitStream; Byte_Value : in Asn1Byte; nBits : in BIT_RANGE; negate : in Boolean)
   is
      Current_Byte :  Integer   := bs.Buffer'First + bs.Current_Bit_Pos / 8;
      cb  : constant BIT_RANGE := bs.Current_Bit_Pos mod 8;
      totalBits : BIT_RANGE;
      totalBitsForNextByte : BIT_RANGE;
      byteValue : Asn1Byte;
   begin
         byteValue := (if negate then (masksb(nBits) and  not Byte_Value) else Byte_Value);
         
         if cb < 8 - nbits then
            totalBits := cb + nBits;
            bs.buffer(Current_Byte) := bs.buffer(Current_Byte) or Shift_left(byteValue, 8 -totalBits);
         else
            totalBitsForNextByte := cb+nbits - 8;
            bs.buffer(Current_Byte) := bs.buffer(Current_Byte) or Shift_right(byteValue, totalBitsForNextByte);
         --bs.currentBytePos := bs.currentBytePos + 1;
            Current_Byte := Current_Byte + 1;
            bs.buffer(Current_Byte) := bs.buffer(Current_Byte) or Shift_left(byteValue, 8 - totalBitsForNextByte);
           
      end if;
      bs.Current_Bit_Pos := bs.Current_Bit_Pos + nBits;
      
   end;
   
   procedure BitStream_Encode_Non_Negative_Integer(bs : in out BitStream; intValue   : in Asn1UInt; nBits : in Integer) 
   is
      byteValue : Asn1Byte;
      tmp : Asn1UInt;
      total_bytes : constant Integer := nBits/8;
      cc : constant BIT_RANGE := nBits mod 8;
      bits_to_shift :Integer;
   begin
      for i in 1 .. total_bytes loop
         bits_to_shift := nBits - (total_bytes- (i - 1))*8;
         tmp := 16#FF# and Shift_right(intValue, bits_to_shift);
         byteValue := Asn1Byte (tmp);

         pragma Loop_Invariant (bs.Current_Bit_Pos = bs.Current_Bit_Pos'Loop_Entry + (i-1)*8);
         
         BitStream_AppendByte(bs, byteValue, false);
      end loop;

      if cc > 0 then
         byteValue := MASKSB(cc) and Asn1Byte(16#FF# and intValue);
         BitStream_AppendPartialByte(bs, byteValue, cc, False);
      end if;

   end;
   
   

   procedure Enc_UInt (bs : in out BitStream;  intValue : in     Asn1UInt;  total_bytes : in Integer)
   is
      byteValue : Asn1Byte;
      tmp : Asn1UInt;
      bits_to_shift :Integer;
   begin

      for i in 1 .. total_bytes loop
         bits_to_shift := total_bytes*8 - (total_bytes-i + 1)*8;
         tmp := 16#FF# and Shift_right(intValue, bits_to_shift);
         byteValue := Asn1Byte (tmp);

         pragma Loop_Invariant (bs.Current_Bit_Pos = bs.Current_Bit_Pos'Loop_Entry + (i-1)*8);
         
         BitStream_AppendByte(bs, byteValue, false);
      end loop;

      
   end Enc_UInt;
   
   
   
   
   -- UPER FUNCTIONS
   procedure UPER_Enc_SemiConstraintWholeNumber
     (bs : in out BitStream;
      IntVal : in     Asn1Int;
      MinVal : in     Asn1Int)
   is
      nBytes             : Asn1Byte;
      ActualEncodedValue : Asn1UInt;
   begin
      ActualEncodedValue := Sub (IntVal, MinVal);

      nBytes := GetBytes (ActualEncodedValue);

      -- encode length
      BitStream_AppendByte (bs, nBytes, False);
      --Encode number
      Enc_UInt (bs, ActualEncodedValue, Integer(nBytes));
   end UPER_Enc_SemiConstraintWholeNumber;   
   
   
   
   
   
   
   
   
   
   
end adaasn1rtl;