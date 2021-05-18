using System;
using System.Threading;
using static System.Console;

/*
 * Title: .Net期中读写锁大作业
 * 
 * Author: 1850170 印昱炜 
 * 
 * Description:
 *   此读写锁主要采用了ManualResetEventSlim实现读锁和写锁。
 *   主要思路为读线程的读操作不加锁，实现并发，写线程使用lock实现独占（保证每次只有一个写锁在写）。
 *   
 *   人工事件用于控制读写分离，当读线程在读时，写线程等待；当有写线程在等待时，读线程等待，直到无写线程在等待并且无写线程在写后才开始读。
 *   
 *   其中涉及到的主要自设变量有readerCounter，writerWait和ifWait。
 *   
 *   readerCounter：当readerCounter为0，说明（1）此时无读线程：写线程直接开始写。
 *                                          （2）此时读线程进行完毕，还有读线程在等待，由于写线程优先级高，等待中的写线程开始写。
 *                                          
 *   writerWait：writerWait记录了当前有多少写线程在等待，只要writerWait不为0，读线程一直等待。
 *   
 *   ifWait：ifWait用于某一读写进程同时开始时的情况，只要此时有写线程在等待，ifWait==1，读线程就无法进入counter_locker这个对象，避免了死锁。
 *   如果不加此变量，会出现某读线程进入counter_locker，判别到ifWait==1，进入等待，而此时等待的写线程无法进入counter_locker，无法释放读锁。
 *   
 *   合理的测试逻辑：读写线程开始，可能先读，可能先写。
 *                   如果先写，则等待所有等待中的写线程结束后，读线程才开始。
 *                   如果先读，则等若干个读线程读完后，出现写线程等待，转而运行写线程，直到所有等待中的写线程结束后，剩余读线程才开始。
 *   
 */

namespace CreateThread
{
    class Program
    {
        public const int readerThreadNum = 50;        //读线程数量
        public const int writerThreadNum = 5;         //写线程数量

        static ManualResetEventSlim _readerEvent = new ManualResetEventSlim(false);   //读者锁，当释放读者锁，读线程开始读，否则等待
        static ManualResetEventSlim _writerEvent = new ManualResetEventSlim(false);   //写者锁，当释放写者锁，写现场开始写，否则等待
        private static void Main(string[] args)
        {
            var readerWriterLocker = new ReaderWriterLocker();   //读写锁对象

            Thread[] reader_threads = new Thread[readerThreadNum];      //读线程数组
            Thread[] writer_threads = new Thread[writerThreadNum];      //写线程数组

            for (int i = 0; i < readerThreadNum; i++)     //创建读线程
            {
                reader_threads[i] = new Thread(readerWriterLocker.ReaderLock);
                reader_threads[i].Name = string.Format("Reader{0}", i);
            }

            for (int i = 0; i < writerThreadNum; i++)     //创建写线程
            {
                writer_threads[i] = new Thread(readerWriterLocker.WriterLock);
                writer_threads[i].Name = string.Format("Writer{0}", i);
            }

            //运行读线程的线程
            Thread readThread = new Thread(() =>
            {
                for (int i = 0; i < 50; i++)
                {
                    reader_threads[i].Start();
                }
            });

            //运行写线程的线程
            Thread writeThread = new Thread(() =>
            {
                for (int i = 0; i < 5; i++)
                {
                    writer_threads[i].Start();
                }
            });

            readThread.Start();    //运行写线程
            writeThread.Start();      //运行读线程

            Read();
        }

        class ReaderWriterLocker
        { 
            int ifWait = 0;      //判断当前是否有写线程在等待（1:是;0:否）
            int readerCounter = 0, writerWait = 0;        //读线程运行的个数，写线程等待的个数
            private readonly object writer_locker = new Object();    //写者排他锁，用于写的独占（通过lock）
            private readonly object counter_locker = new Object();     //计数排他锁，避免读写线程同时开始时计数错乱

            public void Read()    //读操作
            {
                WriteLine("Thread{0} is reading...", Thread.CurrentThread.Name);
                Thread.Sleep(2000);
                WriteLine("Thread{0} finished", Thread.CurrentThread.Name);
                Interlocked.Decrement(ref readerCounter);        //读过程完毕，读线程进行个数-1
            }
            public void Write()    //写操作
            {
                WriteLine("Thread{0} is writing...", Thread.CurrentThread.Name);
                Thread.Sleep(2000);
                WriteLine("Thread{0} finished", Thread.CurrentThread.Name);
            }
            public void ReaderLock()   //读线程进入读锁
            {
                while (ifWait==1) { }      //如果有写线程在等待，则等待（避免死锁）

                lock (counter_locker)
                {
                    if (ifWait == 1)   //如果有线程在等待写，则等待
                    {
                        _readerEvent.Wait();
                        readerCounter++;      //等待结束，读线程数+1
                    }
                    else
                    {
                        readerCounter++;          //此时无写线程，读线程数+1
                    }
                }

                this.Read();     //开始读

                if (readerCounter == 0)    //此时所有在读线程结束
                {
                    _readerEvent.Reset();    //上读锁
                    _writerEvent.Set();   //此时没有线程在读，释放写锁
                }
            }

            public void WriterLock()
            {
                Interlocked.Increment(ref writerWait);    //一旦写线程进入写锁，则写线程等待数+1

                lock (counter_locker)
                {
                    ifWait = 1;   //通知读线程等待

                    if (readerCounter == 0)      //如果没有读线程在读，则释放写锁
                    {
                        _writerEvent.Set();      
                    }
                }

                _writerEvent.Wait();    //如果有读线程在读，则等待

                lock (writer_locker)        //写排他锁
                {
                    writerWait--;   //等待写的线程进入写状态，等待写线程数-1

                    this.Write();   //开始读

                    if (writerWait == 0)        //如果此时没有线程在等待写(此时必然没有线程在写)
                    {
                        ifWait = 0;      //通知读线程可以开始读
                        _readerEvent.Set();      //释放读锁，使读线程能够进入
                        _writerEvent.Reset();      //上写锁
                    }
                }
            }
        }
    }


}
