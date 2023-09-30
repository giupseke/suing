using Microsoft.WindowsAPICodePack.Dialogs;
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
		public string ImageFormat { get; set; } = "";
		public ObservableCollection<string> FileList { get; set; }
		private readonly object FileListLock;
		private string saveFolderName;

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
			ImageFormat = Properties.Settings.Default.ImageFormat;
			folderName.Text = Properties.Settings.Default.SaveFolderName;

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
			Properties.Settings.Default.ImageFormat = ImageFormat;
			Properties.Settings.Default.SaveFolderName = folderName.Text;
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
							if (!FileList.Where(item => item == name).Any())
							{
								FileList.Add(name);
							}
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

		private void OnKeyDown_FileListBox(object sender, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key == System.Windows.Input.Key.Delete)
			{
				var list = new List<string>(FileListBox.SelectedItems.Cast<string>());

				foreach (var item in list)
				{
					if (item == null)
						break;
					_ = FileList.Remove(item as string);
				}
			}
		}

		private void OnClickBrowseButton(object sender, RoutedEventArgs e)
		{
			using CommonOpenFileDialog dialog = new()
			{
				Title = "保存先フォルダ指定",
				InitialDirectory = "",
				IsFolderPicker = true,
			};
			if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
			{
				folderName.Text = dialog.FileName;
				SaveSettings();
			}
		}

		private void OnClickButton(object sender, RoutedEventArgs e)
		{
			saveFolderName = folderName.Text;
			SaveSettings();
			_ = Task.Run(ConvertTask);
		}

		private void ConvertTask()
		{
			_ = buttonConvert.Dispatcher.BeginInvoke(() => IsEnabled = false);
			// TODO: 日本語のみ
			var encoding = Encoding.GetEncoding("shift_jis");
			//var encoding = Encoding.UTF8;
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
					if (saveFolderName != "")
					{
						tmpZipFile = Path.Combine(saveFolderName, Path.GetFileName(tmpZipFile));
					}
					while (true)
					{
						if (!File.Exists(tmpZipFile))
						{
							break;
						}
						var w = Path.GetDirectoryName(tmpZipFile);
						if (w == null) { break; }
						tmpZipFile = Path.Combine(w,
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
			var outFormat = ImageFormat;
			switch (Path.GetExtension(file).ToUpper())
			{
			case ".JPG":
				if (outFormat == "")
					outFormat = "JPEG";
				break;
			case ".PNG":
				if (outFormat == "")
					outFormat = "PNG";
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
			switch (outFormat)
			{
			case "JPEG":
				var parameters = new EncoderParameters(1);
				parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, long.Parse(ImageQuality));
				newImg.Save(file, GetJpegEncoder(), parameters);
				break;
			case "PNG":
				newImg.Save(file, System.Drawing.Imaging.ImageFormat.Png);
				break;
			default:
				Debug.Print($"No Support Format {file}");
				break;
			}
			newImg.Dispose();
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
