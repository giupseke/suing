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
using System.Windows.Controls;
using System.Windows.Data;

namespace suing
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public ObservableCollection<string> FileList { get; set; }
		private readonly object FileListLock;

		public MainWindow()
		{
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

			FileList = new ObservableCollection<string>();
			FileListLock = new object();
			BindingOperations.EnableCollectionSynchronization(FileList, FileListLock);

			InitializeComponent();

			if (Properties.Settings.Default.MainWindow_Left > 0)
			{
				this.Left = Properties.Settings.Default.MainWindow_Left;
				this.Top = Properties.Settings.Default.MainWindow_Top;
				this.Width = Properties.Settings.Default.MainWindow_Width;
				this.Height = Properties.Settings.Default.MainWindow_Height;
			}
			inputWidth.Text = Properties.Settings.Default.ImageWidth;
			inputHeight.Text = Properties.Settings.Default.ImageHeight;
			inputQuality.Text = Properties.Settings.Default.ImageQuality;
			selectFormat.SelectedValue = Properties.Settings.Default.ImageFormat;
			selectOverwrite.SelectedValue = Properties.Settings.Default.SaveOverwrite ? "1" : "0";
			checkClean.IsChecked = Properties.Settings.Default.CleanFolder;
			folderName.Text = Properties.Settings.Default.SaveFolderName;

			DataContext = this;
		}

		private void MainWindow_Close(object sender, EventArgs e)
		{
			SaveSettings();
		}

		private void SaveSettings()
		{
			Properties.Settings.Default.MainWindow_Left = this.RestoreBounds.Left;
			Properties.Settings.Default.MainWindow_Top = this.RestoreBounds.Top;
			Properties.Settings.Default.MainWindow_Width = this.RestoreBounds.Width;
			Properties.Settings.Default.MainWindow_Height = this.RestoreBounds.Height;
			Properties.Settings.Default.ImageWidth = inputWidth.Text;
			Properties.Settings.Default.ImageHeight = inputHeight.Text;
			Properties.Settings.Default.ImageQuality = inputQuality.Text;
			Properties.Settings.Default.ImageFormat = selectFormat.SelectedValue as string;
			Properties.Settings.Default.SaveOverwrite = selectOverwrite.SelectedIndex != 0;
			Properties.Settings.Default.CleanFolder = checkClean.IsChecked ?? false;
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
			var saveFolderName = folderName.Text;
			var imageWidth = int.Parse(inputWidth.Text);
			var imageHeight = int.Parse(inputHeight.Text);
			var quality = long.Parse(inputQuality.Text);
			var format = selectFormat.SelectedValue as string;
			var isclean = checkClean.IsChecked ?? false;
			SaveSettings();
			_ = Task.Run(() => ConvertTask(saveFolderName, imageWidth, imageHeight, quality, format ?? "", isclean));
		}

		private void ConvertTask(string saveFolderName, int targetWidth, int targetHeight, long quality, string format, bool isclean)
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
					var workstr = "";
					var arc = ZipFile.OpenRead(targetFile);
					foreach (var entry in arc.Entries)
					{
						workstr += entry.FullName;
					}
					var b = encoding.GetBytes(workstr);
					var converted = encoding.GetString(b);
					if (converted != workstr)
					{
						encoding = Encoding.UTF8;
					}
					arc.Dispose();

					string tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
					Debug.Print($"{targetFile} to {tempPath}");
					Directory.CreateDirectory(tempPath);

					ZipFile.ExtractToDirectory(targetFile, tempPath, encoding);
					ConvertFolder(tempPath, targetWidth, targetHeight, quality, format, isclean);

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

		private static void ConvertFolder(string folder, int targetWidth, int targetHeight, long quality, string format, bool isclean)
		{
			string[] list = Directory.GetDirectories(folder);
			foreach (var file in list)
			{
				if (Path.GetFileName(file) == "__MACOSX")
				{
					Directory.Delete(file, true);
					continue;
				}
				ConvertFolder(file, targetWidth, targetHeight, quality, format, isclean);
			}

			list = Directory.GetFiles(folder);
			foreach (string file in list)
			{
				ConvertFile(file, targetWidth, targetHeight, quality, format);
			}
		}

		private static void ConvertFile(string file, int targetWidth, int targetHeight, long quality, string outFormat)
		{
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

			var orgImg = System.Drawing.Image.FromFile(file);

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
				parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
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
