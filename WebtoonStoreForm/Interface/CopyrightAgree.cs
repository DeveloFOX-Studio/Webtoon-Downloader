﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WebtoonStoreForm.API;

namespace WebtoonStoreForm.Interface
{
	public partial class CopyrightAgree : Form
	{
		private Point startPoint;
		private Pen lineDrawer = new Pen( Color.DarkGray )
		{
			Width = 1
		};

		public CopyrightAgree( )
		{
			InitializeComponent( );
		}

		private void CLOSE_BUTTON_Click( object sender, EventArgs e )
		{
			Application.Exit( );
		}

		private void AGREE_BUTTON_Click( object sender, EventArgs e )
		{
			if ( NotifyBoxResult.Yes == NotifyBox.Show( this, "약관 동의 확인",
				"이 프로그램의 불법적인 사용으로 인하여 발생되는 모든 법적 문제는 사용자 본인의 책임입니다\n약관에 동의하시겠습니까?", NotifyBoxType.YesNo, NotifyBoxIcon.Warning ) )
			{
				GlobalVar.copyrightAgree = true;
				this.Close( );
			}
		}

		private void DISAGREE_BUTTON_Click( object sender, EventArgs e )
		{
			Application.Exit( );
		}

		private void APP_TITLE_BAR_Paint( object sender, PaintEventArgs e )
		{
			int w = this.APP_TITLE_BAR.Width, h = this.APP_TITLE_BAR.Height;

			e.Graphics.DrawLine( lineDrawer, 0, 0, w, 0 ); // Top line drawing
			e.Graphics.DrawLine( lineDrawer, 0, 0, 0, h ); // Left line drawing
			e.Graphics.DrawLine( lineDrawer, w - lineDrawer.Width, 0, w - lineDrawer.Width, h ); // Right line drawing
			e.Graphics.DrawLine( lineDrawer, 0, h - lineDrawer.Width, w, h - lineDrawer.Width ); // Bottom line drawing
		}

		private void CopyrightAgree_Paint( object sender, PaintEventArgs e )
		{
			int w = this.Width, h = this.Height;

			e.Graphics.DrawLine( lineDrawer, 0, 0, w, 0 ); // Top line drawing
			e.Graphics.DrawLine( lineDrawer, 0, 0, 0, h ); // Left line drawing
			e.Graphics.DrawLine( lineDrawer, w - lineDrawer.Width, 0, w - lineDrawer.Width, h ); // Right line drawing
			e.Graphics.DrawLine( lineDrawer, 0, h - lineDrawer.Width, w, h - lineDrawer.Width ); // Bottom line drawing
		}

		private void APP_TITLE_BAR_MouseDown( object sender, MouseEventArgs e )
		{
			startPoint = e.Location;
		}

		private void APP_TITLE_BAR_MouseMove( object sender, MouseEventArgs e )
		{
			if ( e.Button == MouseButtons.Left )
			{
				this.Location = new Point(
					this.Left - ( startPoint.X - e.X ),
					Math.Max( this.Top - ( startPoint.Y - e.Y ), Screen.FromHandle( this.Handle ).WorkingArea.Top )
				);
			}
		}
	}
}
