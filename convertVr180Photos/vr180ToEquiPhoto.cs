using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Security.Cryptography;

using ExifLibrary;
using JpegParser;

namespace convertVr180Photos {

	class VR180 {

		public static void Main(string[] args) {

			string GetArgument(IEnumerable<string> opts, string option)
				=> opts.SkipWhile(i => i != option).Skip(1).Take(1).FirstOrDefault();

			string inputFileName  = GetArgument(args, "-i");
			string outputFileName = GetArgument(args, "-o");
			string jpegQualityArg = GetArgument(args, "-q");

			long jpegQuality = jpegQualityArg != null
				? long.Parse(jpegQualityArg)
				: 100L;

			if (outputFileName == null || inputFileName == null) {
				Prompt:
				Console.Write("Enter Directory: ");
				var directoryPath = Console.ReadLine();

				if (string.IsNullOrWhiteSpace(directoryPath)) {
					Usage();
					return;
				}

				if (!Directory.Exists(directoryPath)) {
					Console.WriteLine("Invalid directory. Try again.");
					goto Prompt;
				}

				ProcessDirectory(directoryPath);

				return;
			}

			ProcessFile(inputFileName, outputFileName, jpegQuality);
		}

		private static void ProcessDirectory(string directoryPath) {

			var outputDirectoryPath = Path.Combine(directoryPath, "Processed");
			Directory.CreateDirectory(outputDirectoryPath);

			Console.Write("Enter new filename (leave blank for original): ");
			var newFileName = Console.ReadLine().Trim();
			if (newFileName.Length == 0)
				newFileName = null;

			var vrFilePaths = Directory
				.GetFiles(directoryPath, "*.vr.jpg")
				.OrderBy(q => q)
				.ToList();

			var requiredDigits = vrFilePaths.Count.ToString().Length;

			var n = 1;
			foreach (var vrFilePath in vrFilePaths) {

				var numDigits = n.ToString().Length;

				var padding = new string('0', requiredDigits - numDigits);

				var outputFileNameBase = newFileName != null
					? $"{newFileName} {padding}{n}"
					: Path.GetFileName(vrFilePath).Split('.')[0];

				var outputFileName = $"{outputFileNameBase}.sbs.jpg";

				var outputFilePath = Path.Combine(outputDirectoryPath, outputFileName);

				Console.WriteLine($"Processing: {vrFilePath}");
				Console.WriteLine($"    Output: {outputFilePath}");

				ProcessFile(vrFilePath, outputFilePath);
				n++;
			}
		}

		private static void ProcessFile(string inputFileName, string outputFileName, long jpegQuality = 100) { 

			LinkedList<Tuple<string, byte[]>> jpegSegments;
			JpegFile jpegFile = new JpegFile();
			Dictionary<string, string> panoDict;
			byte[] extendedXMPURI = Encoding.UTF8.GetBytes("http://ns.adobe.com/xmp/extension/");
			byte[] zeroByte = { 0x0 };
			byte[] extendedXMPSignature = null;
			string extendXMP = "";

			//            string inputJpeg = args[0];
			//            string outputJpeg = args[1];
			ExifReadWrite exif = new ExifReadWrite();


			using (var stream = File.OpenRead(inputFileName))
				jpegSegments = jpegFile.Parse(stream);

			bool xmpFound = false;
			bool extendedXMPFound = false;

			foreach (var segment in jpegSegments) {

				//Console.WriteLine(segment.Item1);

				if (segment.Item1 == "EXIF") {
					exif.ReadExifAPP1(segment.Item2);
				}

				if (xmpFound != true && segment.Item1 == "APP1") {
					string start = Encoding.UTF8.GetString(segment.Item2, 0, 28);
					if (start == "http://ns.adobe.com/xap/1.0/") {
						// XMP, extract the GPano if its there.
						panoDict = jpegFile.ExtractGPano(Encoding.UTF8.GetString(segment.Item2, 29, segment.Item2.Length - 29));
						string xmpMD5;
						if (panoDict.TryGetValue("xmpNote:HasExtendedXMP", out xmpMD5)) {
							extendedXMPSignature = extendedXMPURI.Concat(zeroByte).Concat(Encoding.UTF8.GetBytes(xmpMD5)).ToArray();
							extendedXMPFound = true;
						}
					}

				}

				if (extendedXMPFound == true && segment.Item1 == "APP1" && jpegFile.segmentCompare(extendedXMPSignature, segment.Item2)) {
					extendXMP += jpegFile.ProcessExtendedXMPSegemnt(segment.Item2, extendXMP.Length);
				}
			}

			var md5 = System.Security.Cryptography.MD5.Create();
			var md5hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(extendXMP));

			var sb = new StringBuilder();

			for (int i = 0; i < md5hash.Length; i++) {
				sb.Append(md5hash[i].ToString("x2"));
			}

			if (extendXMP.Length > 0) {

				var newJpeg = jpegFile.ProcessExtendedXMPXML(extendXMP);
				if (newJpeg != null)
					jpegFile.WriteCombineImage(inputFileName, outputFileName, newJpeg, exif, jpegQuality);
			}
		}

		private static void Usage() {

			Console.WriteLine("Usage: vr180ToEquiPhoto.exe -i vr180Photo.jpg -o equiPhoto.jpg [-q 90]");
			Console.WriteLine("Mono Usage: mono vr180ToEquiPhoto.exe -i vr180Photo.jpg -o equiPhoto.jpg [-q 90]");
			Console.WriteLine("");
			Console.WriteLine("    -i the input file path, a VR180 photo JPEG image, right eye image embedded in the left eye image.");
			Console.WriteLine("");
			Console.WriteLine("    -o the output file path");
			Console.WriteLine("");
			Console.WriteLine("    -q Optional parameter with the jpeg quality setting for the two new jpeg files, 0-100, 0 is very low quailty, 100 should be lossless, defaults to 100");
		}
	}
}
