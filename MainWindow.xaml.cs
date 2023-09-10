using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace suing
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public string ImageWidth { get; set; } = "640";
		public string ImageHeight { get; set; } = "480";
		public string ImageQuality { get; set; } = "90";
		public int FileOverwrite { get; set; } = 1;
		public ObservableCollection<string> FileList { get; set; }
		private readonly object FileListLock;

		public MainWindow()
		{
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

			FileList = new ObservableCollection<string>();
			FileListLock = new object();
			BindingOperations.EnableCollectionSynchronization(FileList, FileListLock);

			InitializeComponent();

			ImageWidth = Properties.Settings.Default.ImageWidth;
			ImageHeight = Properties.Settings.Default.ImageHeight;
			ImageQuality = Properties.Settings.Default.ImageQuality;

			DataContext = this;
		}

		private void MainWindow_Close(object sender, EventArgs e)
		{
			SaveSettings();
		}

		private void SaveSettings()
		{
			Properties.Settings.Default.ImageWidth = ImageWidth;
			Properties.Settings.Default.ImageHeight = ImageHeight;
			Properties.Settings.Default.ImageQuality = ImageQuality;
			Properties.Settings.Default.Save();
		}

		private void OnDrop_FileListBox(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				string[] fileNames = (string[])e.Data.GetData(DataFormats.FileDrop);
				foreach (string name in fileNames)
				{
					switch (Path.GetExtension(name).Trim().ToUpper())
					{
					case ".ZIP":
						lock (FileListLock)
						{
							FileList.Add(name);
						}
						break;
					default:
						_ = MessageBox.Show("未サポートのファイル形式です", "error", MessageBoxButton.OK, MessageBoxImage.Error);
						Debug.Print("No Support Extension " + Path.GetExtension(name));
						break;
					}
				}
			}
		}

		private void OnClickButton(object sender, RoutedEventArgs e)
		{
			SaveSettings();
			_ = Task.Run(ConvertTask);
		}

		private void ConvertTask()
		{
			_ = buttonConvert.Dispatcher.BeginInvoke(() => IsEnabled = false);
			// TODO: 日本語のみ
			var encoding = Encoding.GetEncoding("shift_jis");
			while (true)
			{
				if (FileList.Count <= 0)
				{
					break;
				}

				string targetFile;
				lock (FileListLock)
				{
					targetFile = FileList.First();
					FileList.Remove(targetFile);
				}

				try
				{
					string tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
					Debug.Print($"{targetFile} to {tempPath}");
					Directory.CreateDirectory(tempPath);

					ZipFile.ExtractToDirectory(targetFile, tempPath, encoding);
					ConvertFolder(tempPath);

					string tmpZipFile = targetFile;
					while(true)
					{
						if (!File.Exists(tmpZipFile))
						{
							break;
						}
						tmpZipFile = Path.Combine(Path.GetDirectoryName(tmpZipFile),
							Path.GetFileNameWithoutExtension(tmpZipFile) + "_new" + Path.GetExtension(tmpZipFile));
					}
					ZipFile.CreateFromDirectory(tempPath, tmpZipFile, CompressionLevel.Optimal, false, encoding);

					Directory.Delete(tempPath, true);
				}
				catch (Exception e)
				{
					_ = MessageBox.Show($"ファイル変換に失敗しました\n{targetFile}",
						"error", MessageBoxButton.OK, MessageBoxImage.Error);
					Debug.Print(e.ToString());
					lock (FileListLock)
					{
						FileList.Add(targetFile);
					}
					break;
				}
			}
			_ = buttonConvert.Dispatcher.BeginInvoke(() => IsEnabled = true);
		}

		private void ConvertFolder(string folder)
		{
			string[] list = Directory.GetDirectories(folder);
			foreach (var file in list)
			{
				ConvertFolder(file);
			}

			list = Directory.GetFiles(folder);
			foreach (string file in list)
			{
				ConvertFile(file);
			}
		}

		private void ConvertFile(string file)
		{
			switch (Path.GetExtension(file).ToUpper())
			{
			case ".JPG":
			case ".PNG":
				break;
			default:
				Debug.Print($"No Support Format {file}");
				return;
			}

			var targetWidth = int.Parse(ImageWidth);
			var targetHeight = int.Parse(ImageHeight);
			var orgImg = Image.FromFile(file);

			if (orgImg.Size.Width < targetWidth && orgImg.Size.Height < targetHeight)
			{
				orgImg.Dispose();
				Debug.Print($"No Convert {file}");
				return;
			}

			var newWidth = orgImg.Size.Width;
			var newHeight = orgImg.Size.Height;
			if (orgImg.Size.Width > targetWidth)
			{
				newWidth = targetWidth;
				newHeight = orgImg.Size.Height * targetWidth / orgImg.Size.Width;
			}
			if (newHeight > targetHeight)
			{
				newHeight = targetHeight;
				newWidth = orgImg.Size.Width * targetHeight / orgImg.Size.Height;
			}
			var newImg = new Bitmap(newWidth, newHeight);
			var graphics = Graphics.FromImage(newImg);
			graphics.DrawImage(orgImg, 0, 0, newWidth, newHeight);
			orgImg.Dispose();

			File.Delete(file);
			switch (Path.GetExtension(file).ToUpper())
			{
			case ".JPG":
				var parameters = new EncoderParameters(1);
				parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, long.Parse(ImageQuality));
				newImg.Save(file, GetJpegEncoder(), parameters);
				break;
			case ".PNG":
				newImg.Save(file, ImageFormat.Png);
				break;
			default:
				Debug.Print($"No Support Format {file}");
				break;
			}
			newImg.Dispose();
			newImg = null;
			Debug.Print(file);
		}

		private static ImageCodecInfo GetJpegEncoder()
		{
			try
			{
				return ImageCodecInfo.GetImageEncoders().Where(s => s.MimeType == "image/jpeg").First();
			}
			catch (Exception)
			{
				throw new Exception("jpeg encoder not found");
			}
		}
	}
}
