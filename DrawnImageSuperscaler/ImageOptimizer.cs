﻿using System;
using System.IO;
using ImageMagick;
using System.Threading;
using System.Threading.Tasks;
using System.Configuration;
using System.Collections.Concurrent;

namespace DrawnImageSuperscaler
{
    static class ImageOptimizer
    {
        private static BlockingCollection<string> concurrentImageCollection = new BlockingCollection<string>();
        private static int workingThreads = 0;

        /// <summary>
        /// This function runs asynchronously and checks the Pinger object's BlockingCollection
        /// for new work to do (image optimization), limiting the number of threads used to a set amount.
        /// </summary>
        /// <param name="imageList"></param>
        /// <returns>void</returns>
        public static void ConvertIndividualToPNGAsync(CancellationToken cancellationToken)
        {
            // This loop continually checks the BlockingCollection for new images to be processed.
            Parallel.ForEach(concurrentImageCollection.GetConsumingPartitioner(),
                new ParallelOptions { MaxDegreeOfParallelism = (int)(Environment.ProcessorCount * .75) },
                (curImage, loopState) =>
                {
                    // Optimize the image.
                    Interlocked.Increment(ref workingThreads);
                    OptimizeImage(curImage);
                    // Change the image name to mark it as finished.
                    MarkAsProcessed(curImage);
                    
                    Interlocked.Decrement(ref workingThreads);
                    curImage = null;

                    // If a cancel is requested, wait for remaining work to finish and then leave.
                    if (cancellationToken.IsCancellationRequested)
                    {
                        loopState.Break();
                    }

                    // Give the cpu and IO some breathing room.
                    Thread.Sleep(25);
                });
        }

        /// <summary>
        /// Image conversion worker thread. Converts image at given path
        /// to uncompressed png format.
        /// </summary>
        /// <param name="imagePath"></param>
        private static void OptimizeImage(object imagePath)
        {
            try
            {
                // Convert using imagemagick.
                using (MagickImage imageActual = new MagickImage(new FileInfo((string)imagePath)))
                {
                    // Check to see if the image is properly formatted for the optimizer.
                    // Convert if it isn't.
                    if (imageActual.Format != MagickFormat.Png)
                    {
                        // Remove the file if we are going to overwrite it anyways.
                        File.Delete(Path.GetFullPath((string)imagePath));
                        // Save picture as a png so that we can operate on it.
                        imageActual.Write((string)imagePath, MagickFormat.Png);
                    }
                    // Now compress.
                    ImageMagick.ImageOptimizer optimizer = new ImageMagick.ImageOptimizer();
                    optimizer.OptimalCompression = true;
                    optimizer.LosslessCompress((string)imagePath);
                }
                // Force garbage collect to free up used memory.
                GC.Collect();
            }
            catch (Exception e)
            {
                InnerExceptionPrinter.GetExceptionMessages(e);
            }
            Thread.Sleep(100);
        }

        /// <summary>
        /// Loop through the list and change ☆ to ★ in file name to indicate conversion finished.
        /// If a file with the new name already exists, delete it and then rename the old file.
        /// </summary>
        /// <param name="imageList"></param>
        private static void MarkAsProcessed(string image)
        {
            string newName = null;
            
            newName = image.Replace(char.Parse(ConfigurationManager.AppSettings["UnprocessedImageFlagString"]),
                char.Parse(ConfigurationManager.AppSettings["ProcessedImageFlagString"]));

            // Rename the file.
            // If a file with the new name already exists delete the old file then rename the new one.
            try
            {
                if (File.Exists(newName))
                {
                    File.Delete(newName);
                    File.Move(image, newName);
                }
                else
                {
                    File.Move(image, newName);
                }
            }
            catch (Exception e)
            {
                InnerExceptionPrinter.GetExceptionMessages(e);
            }
        }

        // Helper funcitons for accessing the Pinger classes' image processing queue/collection.
        #region HelperFunctions

        /// <summary>
        /// Helper funciton for enqueueing an image path into Pinger's BlockingCollection for optimization.
        /// </summary>
        /// <param name="imagePath"></param>
        public static void EnqueueImageForOptimization(string imagePath)
        {
            concurrentImageCollection.Add(imagePath);
        }

        /// <summary>
        /// Helper funciton for returning the number of unprocessed images remaining in the BlockingCollection.
        /// </summary>
        /// <returns>Number of unprocessed images.</returns>
        public static int GetPingerImageQueueCount()
        {
            return concurrentImageCollection.Count;
        }

        /// <summary>
        /// 
        /// Helper funciton for getting the number of otpimization threads still active.
        /// </summary>
        /// <returns>Number of active otpimization threads.</returns>
        public static int GetRunningThreadCount()
        {
            return workingThreads;
        }

        public static Partitioner<T> GetConsumingPartitioner<T>(this BlockingCollection<T> collection)
        {
            return new BlockingCollectionPartitioner<T>(collection);
        }

        #endregion HelperFunctions
    }
}
