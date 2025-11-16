using System;
using System.Linq;
using NetSdrClientApp.Messages;
using NUnit.Framework;

namespace NetSdrClientAppTests
{
    [TestFixture]
    public class NetSdrMessageHelperTests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void GetControlItemMessageTest()
        {
            //Arrange
            var type = NetSdrMessageHelper.MsgTypes.Ack;
            var code = NetSdrMessageHelper.ControlItemCodes.ReceiverState;
            int parametersLength = 7500;

            //Act
            byte[] msg = NetSdrMessageHelper.GetControlItemMessage(type, code, new byte[parametersLength]);

            var headerBytes = msg.Take(2);
            var codeBytes = msg.Skip(2).Take(2);
            var parametersBytes = msg.Skip(4);

            var num = BitConverter.ToUInt16(headerBytes.ToArray());
            var actualType = (NetSdrMessageHelper.MsgTypes)(num >> 13);
            var actualLength = num - ((int)actualType << 13);
            var actualCode = BitConverter.ToInt16(codeBytes.ToArray());

            //Assert
            Assert.That(headerBytes.Count(), Is.EqualTo(2));
            Assert.That(msg.Length, Is.EqualTo(actualLength));
            Assert.That(type, Is.EqualTo(actualType));
            Assert.That(actualCode, Is.EqualTo((short)code));
            Assert.That(parametersBytes.Count(), Is.EqualTo(parametersLength));
        }

        [Test]
        public void GetDataItemMessageTest()
        {
            //Arrange
            var type = NetSdrMessageHelper.MsgTypes.DataItem2;
            int parametersLength = 7500;

            //Act
            byte[] msg = NetSdrMessageHelper.GetDataItemMessage(type, new byte[parametersLength]);

            var headerBytes = msg.Take(2);
            var parametersBytes = msg.Skip(2);

            var num = BitConverter.ToUInt16(headerBytes.ToArray());
            var actualType = (NetSdrMessageHelper.MsgTypes)(num >> 13);
            var actualLength = num - ((int)actualType << 13);

            //Assert
            Assert.That(headerBytes.Count(), Is.EqualTo(2));
            Assert.That(msg.Length, Is.EqualTo(actualLength));
            Assert.That(type, Is.EqualTo(actualType));
            Assert.That(parametersBytes.Count(), Is.EqualTo(parametersLength));
        }

        // ----- TranslateMessage -----

        [Test]
        public void TranslateMessage_ControlItem_Success()
        {
            var type = NetSdrMessageHelper.MsgTypes.SetControlItem;
            var code = NetSdrMessageHelper.ControlItemCodes.ReceiverFrequency;
            var parameters = new byte[] { 0x01, 0x02, 0x03 };

            var msg = NetSdrMessageHelper.GetControlItemMessage(type, code, parameters);

            var success = NetSdrMessageHelper.TranslateMessage(
                msg,
                out var parsedType,
                out var parsedCode,
                out var seq,
                out var body);

            Assert.That(success, Is.True);
            Assert.That(parsedType, Is.EqualTo(type));
            Assert.That(parsedCode, Is.EqualTo(code));
            Assert.That(seq, Is.EqualTo(0));
            Assert.That(body, Is.EqualTo(parameters));
        }

        [Test]
        public void TranslateMessage_DataItem_Success()
        {
            var type = NetSdrMessageHelper.MsgTypes.DataItem1;
            ushort sequenceNumber = 123;
            var payload = new byte[] { 10, 20, 30, 40 };

            var parameters = BitConverter.GetBytes(sequenceNumber).Concat(payload).ToArray();
            var msg = NetSdrMessageHelper.GetDataItemMessage(type, parameters);

            var success = NetSdrMessageHelper.TranslateMessage(
                msg,
                out var parsedType,
                out var parsedCode,
                out var seq,
                out var body);

            Assert.That(success, Is.True);
            Assert.That(parsedType, Is.EqualTo(type));
            Assert.That(parsedCode, Is.EqualTo(NetSdrMessageHelper.ControlItemCodes.None));
            Assert.That(seq, Is.EqualTo(sequenceNumber));
            Assert.That(body, Is.EqualTo(payload));
        }

        [Test]
        public void TranslateMessage_InvalidControlItemCode_ReturnsFalse()
        {
            var type = NetSdrMessageHelper.MsgTypes.SetControlItem;
            var code = NetSdrMessageHelper.ControlItemCodes.ReceiverState;
            var parameters = new byte[] { 1, 2, 3, 4 };

            // робимо валідне повідомлення й ламаємо код
            var msg = NetSdrMessageHelper.GetControlItemMessage(type, code, parameters);
            // байти коду знаходяться на позиціях [2..3]
            msg[2] = 0xFF;
            msg[3] = 0xFF;

            var success = NetSdrMessageHelper.TranslateMessage(
                msg,
                out var parsedType,
                out var parsedCode,
                out var seq,
                out var body);

            Assert.That(success, Is.False);
            Assert.That(parsedType, Is.EqualTo(type));
            // код має бути залишений як None
            Assert.That(parsedCode, Is.EqualTo(NetSdrMessageHelper.ControlItemCodes.None));
            Assert.That(seq, Is.EqualTo(0));
            Assert.That(body, Is.EqualTo(parameters));
        }

        [Test]
        public void TranslateMessage_BodyLengthMismatch_ReturnsFalse()
        {
            var type = NetSdrMessageHelper.MsgTypes.Ack;
            var code = NetSdrMessageHelper.ControlItemCodes.ReceiverState;
            var parameters = new byte[] { 1, 2, 3, 4, 5 };

            var msg = NetSdrMessageHelper.GetControlItemMessage(type, code, parameters);
            // обрізаємо останній байт
            var truncated = msg.Take(msg.Length - 1).ToArray();

            var success = NetSdrMessageHelper.TranslateMessage(
                truncated,
                out var parsedType,
                out var parsedCode,
                out var seq,
                out var body);

            Assert.That(success, Is.False);
            Assert.That(parsedType, Is.EqualTo(type));
            Assert.That(parsedCode, Is.EqualTo(code));
            Assert.That(body.Length, Is.EqualTo(parameters.Length - 1));
        }

        // ----- GetSamples -----

        [Test]
        public void GetSamples_ReturnsExpectedValues_For16BitSamples()
        {
            // два 16-бітових значення: 1 і 2 (little-endian)
            var body = new byte[] { 0x01, 0x00, 0x02, 0x00 };

            var samples = NetSdrMessageHelper.GetSamples(16, body).ToArray();

            Assert.That(samples.Length, Is.EqualTo(2));
            Assert.That(samples[0], Is.EqualTo(1));
            Assert.That(samples[1], Is.EqualTo(2));
        }

        [Test]
        public void GetSamples_Throws_WhenSampleSizeTooLarge()
        {
            var body = new byte[10];

            Assert.That(
                () => NetSdrMessageHelper.GetSamples(40, body).ToArray(),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        // ----- Length / header edge cases -----

        [Test]
        public void GetControlItemMessage_TooLongParameters_ThrowsArgumentException()
        {
            var type = NetSdrMessageHelper.MsgTypes.SetControlItem;
            var code = NetSdrMessageHelper.ControlItemCodes.ReceiverState;

            // явне перевищення максимальної довжини
            var parameters = new byte[9000];

            Assert.That(
                () => NetSdrMessageHelper.GetControlItemMessage(type, code, parameters),
                Throws.TypeOf<ArgumentException>()
                      .With.Message.Contains("Message length exceeds allowed value"));
        }

        [Test]
        public void GetDataItemMessage_TooLongParameters_ThrowsArgumentException()
        {
            var type = NetSdrMessageHelper.MsgTypes.DataItem0;

            // >8192 байт параметрів → довжина з заголовком > 8194 → виняток
            var parameters = new byte[9000];

            Assert.That(
                () => NetSdrMessageHelper.GetDataItemMessage(type, parameters),
                Throws.TypeOf<ArgumentException>()
                      .With.Message.Contains("Message length exceeds allowed value"));
        }

        [Test]
        public void DataItem_MaxLength_EdgeCase_IsHandledCorrectly()
        {
            var type = NetSdrMessageHelper.MsgTypes.DataItem0;

            // 8192 байти параметрів — той самий edge-case, де довжина в заголовку кодується як 0
            var parameters = new byte[8192];
            ushort seq = 0x1234;
            // перші 2 байти параметрів — sequence number, решта — тіло
            BitConverter.GetBytes(seq).CopyTo(parameters, 0);

            var msg = NetSdrMessageHelper.GetDataItemMessage(type, parameters);

            var success = NetSdrMessageHelper.TranslateMessage(
                msg,
                out var parsedType,
                out var parsedCode,
                out var parsedSeq,
                out var body);

            Assert.That(success, Is.True);
            Assert.That(parsedType, Is.EqualTo(type));
            Assert.That(parsedCode, Is.EqualTo(NetSdrMessageHelper.ControlItemCodes.None));
            Assert.That(parsedSeq, Is.EqualTo(seq));
            // повинен «відкусити» sequenceNumber, але все ще збігатися по довжині
            Assert.That(body.Length, Is.EqualTo(parameters.Length - 2));
        }
    }
}
