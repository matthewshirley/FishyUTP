﻿// Copyright (c) 2021 Unity Technologies
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies
// or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//
// License: https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/blob/develop/com.unity.netcode.gameobjects/LICENSE.md


using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport;

// This implementation was borrowed from Unity Netcode for GameObjects as way
// to work around the 44kb limit that reliable pipelines impose.
namespace FishNet.Transporting.FishyUTPPlugin
{
    /// <summary>Queue for batched messages meant to be sent through UTP.</summary>
    /// <remarks>
    /// Messages should be pushed on the queue with <see cref="PushMessage"/>. To send batched
    /// messages, call <see cref="FillWriterWithMessages"/> or <see cref="FillWriterWithBytes"/>
    /// with the <see cref="DataStreamWriter"/> obtained from <see cref="NetworkDriver.BeginSend"/>.
    /// This will fill the writer with as many messages/bytes as possible. If the send is
    /// successful, call <see cref="Consume"/> to remove the data from the queue.
    ///
    /// This is meant as a companion to <see cref="BatchedReceiveQueue"/>, which should be used to
    /// read messages sent with this queue.
    /// </remarks>
    public struct BatchedSendQueue : IDisposable
    {
        // Note that we're using NativeList basically like a growable NativeArray, where the length
        // of the list is the capacity of our array. (We can't use the capacity of the list as our
        // queue capacity because NativeList may elect to set it higher than what we'd set it to
        // with SetCapacity, which breaks the logic of our code.)
        private NativeList<byte> m_Data;
        private NativeArray<int> m_HeadTailIndices;
        private int m_MaximumCapacity;
        private int m_MinimumCapacity;

        /// <summary>Overhead that is added to each message in the queue.</summary>
        public const int PerMessageOverhead = sizeof(int);

        internal const int MinimumMinimumCapacity = 4096;

        // Indices into m_HeadTailIndicies.
        private const int k_HeadInternalIndex = 0;
        private const int k_TailInternalIndex = 1;

        /// <summary>Index of the first byte of the oldest data in the queue.</summary>
        private int HeadIndex
        {
            get { return m_HeadTailIndices[k_HeadInternalIndex]; }
            set { m_HeadTailIndices[k_HeadInternalIndex] = value; }
        }

        /// <summary>Index one past the last byte of the most recent data in the queue.</summary>
        private int TailIndex
        {
            get { return m_HeadTailIndices[k_TailInternalIndex]; }
            set { m_HeadTailIndices[k_TailInternalIndex] = value; }
        }

        public int Length => TailIndex - HeadIndex;
        public int Capacity => m_Data.Length;
        public bool IsEmpty => HeadIndex == TailIndex;
        public bool IsCreated => m_Data.IsCreated;

        /// <summary>Construct a new empty send queue.</summary>
        /// <param name="capacity">Maximum capacity of the send queue.</param>
        public BatchedSendQueue(int capacity)
        {
            // Make sure the maximum capacity will be even.
            m_MaximumCapacity = capacity + (capacity & 1);

            // We pick the minimum capacity such that if we keep doubling it, we'll eventually hit
            // the maximum capacity exactly. The alternative would be to use capacities that are
            // powers of 2, but this can lead to over-allocating quite a bit of memory (especially
            // since we expect maximum capacities to be in the megabytes range). The approach taken
            // here avoids this issue, at the cost of not having allocations of nice round sizes.
            m_MinimumCapacity = m_MaximumCapacity;
            while (m_MinimumCapacity / 2 >= MinimumMinimumCapacity)
            {
                m_MinimumCapacity /= 2;
            }

            m_Data = new NativeList<byte>(m_MinimumCapacity, Allocator.Persistent);
            m_HeadTailIndices = new NativeArray<int>(2, Allocator.Persistent);

            m_Data.ResizeUninitialized(m_MinimumCapacity);

            HeadIndex = 0;
            TailIndex = 0;
        }

        public void Dispose()
        {
            if (IsCreated)
            {
                m_Data.Dispose();
                m_HeadTailIndices.Dispose();
            }
        }

        /// <summary>Write a raw buffer to a DataStreamWriter.</summary>
        private unsafe void WriteBytes(ref DataStreamWriter writer, byte* data, int length)
        {
            writer.WriteBytes(data, length);
        }

        /// <summary>Append data at the tail of the queue. No safety checks.</summary>
        private void AppendDataAtTail(ArraySegment<byte> data)
        {
            unsafe
            {
                var writer = new DataStreamWriter((byte*)m_Data.GetUnsafePtr() + TailIndex, Capacity - TailIndex);

                writer.WriteInt(data.Count);

                fixed (byte* dataPtr = data.Array)
                {
                    WriteBytes(ref writer, dataPtr + data.Offset, data.Count);
                }
            }

            TailIndex += sizeof(int) + data.Count;
        }

        /// <summary>Append a new message to the queue.</summary>
        /// <param name="message">Message to append to the queue.</param>
        /// <returns>
        /// Whether the message was appended successfully. The only way it can fail is if there's
        /// no more room in the queue. On failure, nothing is written to the queue.
        /// </returns>
        public bool PushMessage(ArraySegment<byte> message)
        {
            if (!IsCreated)
            {
                return false;
            }

            // Check if there's enough room after the current tail index.
            if (Capacity - TailIndex >= sizeof(int) + message.Count)
            {
                AppendDataAtTail(message);
                return true;
            }

            // Move the data at the beginning of of m_Data. Either it will leave enough space for
            // the message, or we'll grow m_Data and will want the data at the beginning anyway.
            if (HeadIndex > 0 && Length > 0)
            {
                unsafe
                {
                    UnsafeUtility.MemMove(m_Data.GetUnsafePtr(), (byte*)m_Data.GetUnsafePtr() + HeadIndex, Length);
                }

                TailIndex = Length;
                HeadIndex = 0;
            }

            // If there's enough space left at the end for the message, now is a good time to trim
            // the capacity of m_Data if it got very large. We define "very large" here as having
            // more than 75% of m_Data unused after adding the new message.
            if (Capacity - TailIndex >= sizeof(int) + message.Count)
            {
                AppendDataAtTail(message);

                while (TailIndex < Capacity / 4 && Capacity > m_MinimumCapacity)
                {
                    m_Data.ResizeUninitialized(Capacity / 2);
                }

                return true;
            }

            // If we get here we need to grow m_Data until the data fits (or it's too large).
            while (Capacity - TailIndex < sizeof(int) + message.Count)
            {
                // Can't grow m_Data anymore. Message simply won't fit.
                if (Capacity * 2 > m_MaximumCapacity)
                {
                    return false;
                }

                m_Data.ResizeUninitialized(Capacity * 2);
            }

            // If we get here we know there's now enough room for the message.
            AppendDataAtTail(message);
            return true;
        }

        /// <summary>
        /// Fill as much of a <see cref="DataStreamWriter"/> as possible with data from the head of
        /// the queue. Only full messages (and their length) are written to the writer.
        /// </summary>
        /// <remarks>
        /// This does NOT actually consume anything from the queue. That is, calling this method
        /// does not reduce the length of the queue. Callers are expected to call
        /// <see cref="Consume"/> with the value returned by this method afterwards if the data can
        /// be safely removed from the queue (e.g. if it was sent successfully).
        ///
        /// This method should not be used together with <see cref="FillWriterWithBytes"> since this
        /// could lead to a corrupted queue.
        /// </remarks>
        /// <param name="writer">The <see cref="DataStreamWriter"/> to write to.</param>
        /// <returns>How many bytes were written to the writer.</returns>
        public int FillWriterWithMessages(ref DataStreamWriter writer)
        {
            if (!IsCreated || Length == 0)
            {
                return 0;
            }

            unsafe
            {
                var reader = new DataStreamReader(m_Data.AsArray());

                var writerAvailable = writer.Capacity;
                var readerOffset = HeadIndex;

                while (readerOffset < TailIndex)
                {
                    reader.SeekSet(readerOffset);
                    var messageLength = reader.ReadInt();

                    if (writerAvailable < sizeof(int) + messageLength)
                    {
                        break;
                    }
                    else
                    {
                        writer.WriteInt(messageLength);

                        var messageOffset = HeadIndex + reader.GetBytesRead();
                        WriteBytes(ref writer, (byte*)m_Data.GetUnsafePtr() + messageOffset, messageLength);

                        writerAvailable -= sizeof(int) + messageLength;
                        readerOffset += sizeof(int) + messageLength;
                    }
                }

                return writer.Capacity - writerAvailable;
            }
        }

        /// <summary>
        /// Fill the given <see cref="DataStreamWriter"/> with as many bytes from the queue as
        /// possible, disregarding message boundaries.
        /// </summary>
        ///<remarks>
        /// This does NOT actually consume anything from the queue. That is, calling this method
        /// does not reduce the length of the queue. Callers are expected to call
        /// <see cref="Consume"/> with the value returned by this method afterwards if the data can
        /// be safely removed from the queue (e.g. if it was sent successfully).
        ///
        /// This method should not be used together with <see cref="FillWriterWithMessages"/> since
        /// this could lead to reading messages from a corrupted queue.
        /// </remarks>
        /// <param name="writer">The <see cref="DataStreamWriter"/> to write to.</param>
        /// <returns>How many bytes were written to the writer.</returns>
        public int FillWriterWithBytes(ref DataStreamWriter writer)
        {
            if (!IsCreated || Length == 0)
            {
                return 0;
            }

            var copyLength = Math.Min(writer.Capacity, Length);

            unsafe
            {
                WriteBytes(ref writer, (byte*)m_Data.GetUnsafePtr() + HeadIndex, copyLength);
            }

            return copyLength;
        }

        /// <summary>Consume a number of bytes from the head of the queue.</summary>
        /// <remarks>
        /// This should only be called with a size that matches the last value returned by
        /// <see cref="FillWriter"/>. Anything else will result in a corrupted queue.
        /// </remarks>
        /// <param name="size">Number of bytes to consume from the queue.</param>
        public void Consume(int size)
        {
            // Adjust the head/tail indices such that we consume the given size.
            if (size >= Length)
            {
                HeadIndex = 0;
                TailIndex = 0;

                // This is a no-op if m_Data is already at minimum capacity.
                m_Data.ResizeUninitialized(m_MinimumCapacity);
            }
            else
            {
                HeadIndex += size;
            }
        }
    }
}
