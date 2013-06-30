/*
 * Copyright (C) 2010 Colin Mackie.
 * This software is distributed under the terms of the GNU General Public License.
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Web;

using ZXing;

namespace WinAuth
{
	/// <summary>
	/// Form class for create a new Battle.net authenticator
	/// </summary>
	public partial class AddGoogleAuthenticator : Form
	{
		/// <summary>
		/// Form instantiation
		/// </summary>
		public AddGoogleAuthenticator()
		{
			InitializeComponent();
		}

		/// <summary>
		/// Current authenticator
		/// </summary>
		public WinAuthAuthenticator Authenticator { get; protected set; }

#region Form Events

		/// <summary>
		/// Load the form
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void AddGoogleAuthenticator_Load(object sender, EventArgs e)
		{
		}


		/// <summary>
		/// Click the OK button to verify and add the authenticator
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void okButton_Click(object sender, EventArgs e)
		{
			string privatekey = this.secretCodeField.Text.Trim();
			if (privatekey.Length == 0)
			{
				WinAuthForm.ErrorDialog(this.Owner, "Please enter the Secret Code");
				this.DialogResult = System.Windows.Forms.DialogResult.None;
				return;
			}
			if (verifyAuthenticator(privatekey) == false)
			{
				this.DialogResult = System.Windows.Forms.DialogResult.None;
				return;
			}
		}

		/// <summary>
		/// Click button to show QR code
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void showQrCodeButton_Click(object sender, EventArgs e)
		{
			string privatekey = this.secretCodeField.Text.Trim();
			if (string.IsNullOrEmpty(privatekey) == false)
			{
				verifyAuthenticator(privatekey);
			}
		}

#endregion

#region Private methods

		/// <summary>
		/// Check if a filename is valid and such a file exists
		/// </summary>
		/// <param name="filename">filename to check</param>
		/// <returns>true if valid and exists</returns>
		private bool IsValidFile(string filename)
		{
			try
			{
				// check path is valid
				new FileInfo(filename);
				return File.Exists(filename);
			}
			catch (Exception) { }

			return false;
		}

		/// <summary>
		/// Verify and create the authenticator if needed
		/// </summary>
		/// <returns>true is successful</returns>
		private bool verifyAuthenticator(string privatekey)
		{
			if (string.IsNullOrEmpty(privatekey) == true)
			{
				return false;
			}

			WinAuthAuthenticator authenticator = new WinAuthAuthenticator();
			string authtype = "totp";

			// if this is a URL, pull it down
			Uri uri;
			Match match;
			if (Regex.IsMatch(privatekey, "https?://.*") == true && Uri.TryCreate(privatekey, UriKind.Absolute, out uri) == true)
			{
				try
				{
					var request = (HttpWebRequest)WebRequest.Create(uri);
					request.AllowAutoRedirect = true;
					request.Timeout = 20000;
					request.UserAgent = "Mozilla/4.0 (compatible; MSIE 8.0; Windows NT 6.1; Trident/4.0)";
					using (var response = (HttpWebResponse)request.GetResponse())
					{
						if (response.StatusCode == HttpStatusCode.Accepted && response.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
						{
							using (Bitmap bitmap = (Bitmap)Bitmap.FromStream(response.GetResponseStream()))
							{
								IBarcodeReader reader = new BarcodeReader();
								var result = reader.Decode(bitmap);
								if (result != null)
								{
									privatekey = result.Text;
								}
							}
						}
					}
				}
				catch (Exception ex)
				{
					WinAuthForm.ErrorDialog(this.Owner, "Cannot load QR code image from " + privatekey, ex);
					return false;
				}
			}
			else if ((match = Regex.Match(privatekey, @"data:image/([^;]+);base64,(.*)", RegexOptions.IgnoreCase)).Success == true)
			{
				byte[] imagedata = Convert.FromBase64String(match.Groups[2].Value);
				using (MemoryStream ms = new MemoryStream(imagedata))
				{
					using (Bitmap bitmap = (Bitmap)Bitmap.FromStream(ms))
					{
						IBarcodeReader reader = new BarcodeReader();
						var result = reader.Decode(bitmap);
						if (result != null)
						{
							privatekey = result.Text;
						}
					}
				}
			}
			else if (IsValidFile(privatekey) == true)
			{
				// assume this is the image file
				using (Bitmap bitmap = (Bitmap)Bitmap.FromFile(privatekey))
				{
					IBarcodeReader reader = new BarcodeReader();
					var result = reader.Decode(bitmap);
					if (result != null)
					{
						privatekey = result.Text;
					}
				}
			}

			// check for otpauth://, e.g. "otpauth://totp/dc3bf64c-2fd4-40fe-a8cf-83315945f08b@blockchain.info?secret=IHZJDKAEEC774BMUK3GX6SA"
			match = Regex.Match(privatekey, @"otpauth://([^/]+)/([^?]+)\?(.*)", RegexOptions.IgnoreCase);
			if (match.Success == true)
			{
				authtype = match.Groups[1].Value; // @todo we only handle totp (not hotp)
				string label = match.Groups[2].Value;
				if (string.IsNullOrEmpty(label) == false)
				{
					authenticator.Name = label;
				}

				NameValueCollection qs = HttpUtility.ParseQueryString(match.Groups[3].Value);
				privatekey = qs["secret"] ?? privatekey;
			}

			// just get the hex chars
			privatekey = Regex.Replace(privatekey, @"[^0-9a-z]", "", RegexOptions.IgnoreCase);
			if (privatekey.Length == 0)
			{
				WinAuthForm.ErrorDialog(this.Owner, "The secret code is not valid");
				return false;
			}

			try
			{
				GoogleAuthenticator auth = new GoogleAuthenticator();
				auth.Enroll(privatekey);
				auth.Sync();
				authenticator.AuthenticatorData = auth;
				this.Authenticator = authenticator;

				string url = "otpauth://" + authtype + "/" + authenticator.Name + "?secret=" + privatekey;
				BarcodeWriter writer = new BarcodeWriter();
				writer.Format = BarcodeFormat.QR_CODE;
				writer.Options = new ZXing.Common.EncodingOptions { Width = qrImage.Width, Height = qrImage.Height, Margin = 0 };
				qrImage.Image = writer.Write(url);

				string summary = string.Empty;
				if (string.IsNullOrEmpty(authenticator.Name) == false)
				{
					summary += authenticator.Name + Environment.NewLine;
				}
				summaryInfoLabel.Text = summary;
			}
			catch (Exception irre)
			{
				WinAuthForm.ErrorDialog(this.Owner, "Unable to create the authenticator. The secret code is probably invalid.", irre);
				return false;
			}

			return true;
		}

#endregion

	}
}