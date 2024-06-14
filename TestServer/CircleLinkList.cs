using System;
using System.Collections.Generic;
using FFmpeg.AutoGen;
using TestServer;

public class CircleLinkList<T> where T : VideoFrame, new()
{
    private Node<T> head;
    public Node<T> Current { get; private set; }
    public int Count { get; private set; }
    private int capacity;//超过这个容量后就直接覆盖已有的节点

    public CircleLinkList(int capacity)
    {
        if (capacity <= 0) throw new ArgumentException("Capacity must be greater than 0");
        this.capacity = capacity;
    }

    public unsafe void Add(T value)
    {
        if (Count < capacity)
        {
            Node<T> newNode = new Node<T>(value);
            Count++;

            if (Current == null)
            {
                Current = newNode;
                newNode.Next = newNode;
                newNode.Prev = newNode;
                head = Current;
            }
            else
            {
                Current.Next = newNode;
                newNode.Prev = Current;
                newNode.Next = head;
                head.Prev = newNode;
                Current = newNode;
            }
        }
        else
        {
            //后移一个节点
            Current = Current.Next;
            var oldVal = Current.Value as VideoFrame;
            if (oldVal != null)
            {
                AVFrame aVFrame = oldVal.AVFrame;
                AVFrame* framePtr = &aVFrame;  // 使用取地址符获取指针
                //ffmpeg.av_frame_free(&framePtr);
                ffmpeg.av_freep(framePtr);
            }
            Current.Value = null;
            Current.Value = value;
        }
    }

    public void Remove(T value)
    {
        if (Current == null)// 空链表
            return;

        //当前节点就是要删除的节点
        if (Current.Value.Equals(value))
        {
            Node<T> nodenext = Current.Next;
            Current = Current.Prev;
            Current.Next = nodenext;
            nodenext.Prev = Current;
            Count--;
            return;
        }

        Node<T> search = Current;

        //查找要删除的节点的前一个节点
        // 查找要删除的节点
        do
        {
            if (search.Value.Equals(value))
                break;
            search = search.Prev;
        }
        while (Current.Next != search);

        // 未找到要删除的节点
        if (Current.Next == search)
            return;

        if (head == search) head = search.Next;
        // 删除中间节点
        Node<T> removeNodeNext = search.Next;
        search.Prev.Next = removeNodeNext;
        removeNodeNext.Prev = search.Prev;
        Count--;
        return;
    }

    public IEnumerable<T> Filter(Func<T, bool> filterStart, Func<T, bool> filterEnd)
    {
        if (Current == null) yield break;
        Node<T> current = Current.Prev;
        do
        {
            if (filterStart(current.Prev.Value))
            {
                current = current.Prev;
            }
            else
            {
                break;
            }
        } while (Current != current);
        Node<T> firstItem = current;
        if (Current == current)//走到循环头了，头是最后一帧
        {
            firstItem = current.Next;
        }
        Console.WriteLine("firstItem-" + firstItem.Value);
        do
        {
            if (filterStart(current.Value) && filterEnd(current.Value))
            {
                yield return current.Value;
                current = current.Next;
            }
            else
            {
                break;
            }
        }
        while (firstItem != current);
    }

    public void Display()
    {
        Node<T> current = Current;
        do   // 循环输出
        {
            Console.WriteLine(Current.Value);
            current = current.Prev;
        }
        while (Current.Next != current);
    }
}

public class Node<T>
{
    public T Value { get; set; }
    public Node<T> Next { get; set; }
    public Node<T> Prev { get; set; }

    public Node(T value)
    {
        Value = value;
        Next = null;
        Prev = null;
    }
}
