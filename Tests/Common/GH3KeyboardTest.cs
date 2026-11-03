// @under-test: Plugins/GH3Keyboard/GH3Keyboard.cs
// @area: mapping   @layer: unit
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using x360ce.Plugins;
using x360ce.Plugins.GH3;

namespace x360ce.Tests
{
	/// <summary>
	/// The GH3 Keyboard plugin plays Guitar Hero III with five keys and strums by itself. Its strum logic is driven here
	/// with a clock and keys the test gives it, one read a millisecond as x360ce reads at its default rate, so each rule
	/// the game depends on is pinned: one strum per new note or chord, none for a held or a released key, a pulse that
	/// always ends, and frets held while the strum is out.
	/// </summary>
	/// <remarks>Passing here does not show the game takes it: that is checked in Guitar Hero III itself.</remarks>
	[TestClass]
	public class GH3KeyboardTest
	{
		#region Helpers

		const int G = AutoStrum.Green, R = AutoStrum.Red, Y = AutoStrum.Yellow, B = AutoStrum.Blue, O = AutoStrum.Orange;

		/// <summary>The strum logic with the values the plugin starts with: down, 10 ms chord window, 30 ms pulse, 20 ms gap.</summary>
		static AutoStrum NewStrum(StrumDirection direction = StrumDirection.Down)
		{
			return new AutoStrum { Direction = direction, ChordWindowMs = 10, PulseMs = 30, GapMs = 20 };
		}

		/// <summary>What the controller held at each read.</summary>
		sealed class Trace
		{
			public readonly List<int> Frets = new List<int>();
			public readonly List<StrumOutput> Strums = new List<StrumOutput>();

			/// <summary>The reads at which a strum began: where it went from none, or turned to the other way.</summary>
			public List<int> StrumStarts()
			{
				var starts = new List<int>();
				for (var i = 0; i < Strums.Count; i++)
				{
					var before = i == 0 ? StrumOutput.None : Strums[i - 1];
					if (Strums[i] != StrumOutput.None && Strums[i] != before)
						starts.Add(i);
				}
				return starts;
			}
		}

		/// <summary>Reads once a millisecond from <paramref name="from"/> to <paramref name="to"/>, with the keys <paramref name="keys"/> gives for each time.</summary>
		static void Run(AutoStrum strum, Trace trace, int from, int to, Func<int, int> keys)
		{
			for (var t = from; t < to; t++)
			{
				strum.Update(t, keys(t));
				trace.Frets.Add(strum.Frets);
				trace.Strums.Add(strum.Strum);
			}
		}

		#endregion

		#region One note

		[TestMethod, TestCategory("mapping"), TestCategory("critical")]
		[Description("A key pressed holds its fret at once and strums once, when the chord window closes")]
		public void A_note_holds_its_fret_at_once_and_strums_once()
		{
			var strum = NewStrum();
			var trace = new Trace();
			Run(strum, trace, 0, 500, t => t >= 100 ? G : 0);
			Assert.AreEqual(G, trace.Frets[100], "The fret goes out on the read the key is seen down: no delay.");
			CollectionAssert.AreEqual(new[] { 110 }, trace.StrumStarts(), "One strum, when the 10 ms window closes.");
			Assert.AreEqual(1, strum.StrumCount);
		}

		[TestMethod, TestCategory("mapping"), TestCategory("critical")]
		[Description("A key held for seconds strums once, and the strum lets go after its pulse")]
		public void A_held_key_strums_once_and_the_strum_lets_go()
		{
			var strum = NewStrum();
			var trace = new Trace();
			// Held for three seconds: as long as a sustained note. The keyboard's own repeat does not reach the plugin,
			// which reads the key's state, so this is also what a repeating key looks like to it.
			Run(strum, trace, 0, 3000, t => G);
			Assert.AreEqual(1, trace.StrumStarts().Count, "A held key is one note, not many.");
			Assert.AreEqual(StrumOutput.Down, trace.Strums[10]);
			Assert.AreEqual(StrumOutput.Down, trace.Strums[39], "The pulse lasts 30 ms.");
			Assert.AreEqual(StrumOutput.None, trace.Strums[40], "Then the strum lets go, so a held key does not hold it.");
			Assert.AreEqual(StrumOutput.None, trace.Strums[2999]);
			Assert.AreEqual(G, trace.Frets[2999], "The fret stays held while the key is, for a sustained note.");
		}

		[TestMethod, TestCategory("mapping")]
		[Description("Letting a key go never strums")]
		public void Releasing_a_key_does_not_strum()
		{
			var strum = NewStrum();
			var trace = new Trace();
			Run(strum, trace, 0, 1000, t => t < 300 ? G | R : t < 600 ? G : 0);
			Assert.AreEqual(1, trace.StrumStarts().Count, "Only the chord pressed at the start strums.");
			Assert.AreEqual(0, trace.Frets[999]);
		}

		[TestMethod, TestCategory("mapping")]
		[Description("A key tapped for less than the chord window still reaches the game with its strum")]
		public void A_short_tap_keeps_its_fret_through_the_strum()
		{
			var strum = NewStrum();
			var trace = new Trace();
			// Down for 3 ms only, shorter than the 10 ms window the strum waits.
			Run(strum, trace, 0, 200, t => t >= 50 && t < 53 ? Y : 0);
			var starts = trace.StrumStarts();
			Assert.AreEqual(1, starts.Count);
			for (var t = 50; t < starts[0] + 30; t++)
				Assert.AreEqual(Y, trace.Frets[t], "The fret is held until the strum ends, read " + t + ".");
			Assert.AreEqual(0, trace.Frets[starts[0] + 30], "Then it lets go, as the key already did.");
		}

		[TestMethod, TestCategory("mapping")]
		[Description("With no chord window the strum goes out on the read the key is seen")]
		public void No_window_strums_at_once()
		{
			var strum = NewStrum();
			strum.ChordWindowMs = 0;
			var trace = new Trace();
			Run(strum, trace, 0, 100, t => t >= 20 ? O : 0);
			CollectionAssert.AreEqual(new[] { 20 }, trace.StrumStarts());
			Assert.AreEqual(O, trace.Frets[20]);
		}

		#endregion

		#region Chords

		[TestMethod, TestCategory("mapping"), TestCategory("critical")]
		[Description("Keys of a chord pressed a few milliseconds apart make one strum with every fret held")]
		public void A_chord_strums_once_with_every_fret()
		{
			var strum = NewStrum();
			var trace = new Trace();
			// Green, then red 4 ms later, then yellow 8 ms later: all inside the 10 ms window.
			Run(strum, trace, 0, 300, t => (t >= 100 ? G : 0) | (t >= 104 ? R : 0) | (t >= 108 ? Y : 0));
			var starts = trace.StrumStarts();
			CollectionAssert.AreEqual(new[] { 110 }, starts, "One strum for the chord, not one for each key.");
			Assert.AreEqual(G | R | Y, trace.Frets[starts[0]], "Every fret of the chord is held as the strum goes out.");
		}

		[TestMethod, TestCategory("mapping")]
		[Description("Chords of two, three, four and five notes each strum once")]
		public void Chords_of_every_size_strum_once()
		{
			foreach (var chord in new[] { G | R, G | R | Y, G | R | Y | B, G | R | Y | B | O })
			{
				var strum = NewStrum();
				var trace = new Trace();
				Run(strum, trace, 0, 200, t => t >= 50 ? chord : 0);
				var starts = trace.StrumStarts();
				Assert.AreEqual(1, starts.Count, "Chord " + chord + ".");
				Assert.AreEqual(chord, trace.Frets[starts[0]], "Chord " + chord + ".");
			}
		}

		[TestMethod, TestCategory("mapping")]
		[Description("A key pressed after the chord window closed is a new note, with a strum of its own")]
		public void A_key_late_for_the_window_is_a_new_note()
		{
			// What the tolerance setting is for: 15 ms apart is two notes at 10 ms, one chord at 20 ms.
			var balanced = NewStrum();
			var trace = new Trace();
			Run(balanced, trace, 0, 300, t => (t >= 100 ? G : 0) | (t >= 115 ? R : 0));
			Assert.AreEqual(2, trace.StrumStarts().Count, "Balanced: 15 ms apart is two notes.");
			var tolerant = NewStrum();
			tolerant.ChordWindowMs = AutoStrum.WindowOf(ChordTolerance.Tolerant);
			trace = new Trace();
			Run(tolerant, trace, 0, 300, t => (t >= 100 ? G : 0) | (t >= 115 ? R : 0));
			Assert.AreEqual(1, trace.StrumStarts().Count, "Tolerant: 15 ms apart is one chord.");
		}

		[TestMethod, TestCategory("mapping")]
		[Description("The three tolerances keep their windows")]
		public void Tolerances_have_their_windows()
		{
			Assert.AreEqual(5.0, AutoStrum.WindowOf(ChordTolerance.Faster));
			Assert.AreEqual(10.0, AutoStrum.WindowOf(ChordTolerance.Balanced));
			Assert.AreEqual(20.0, AutoStrum.WindowOf(ChordTolerance.Tolerant));
		}

		#endregion

		#region Notes one after another

		[TestMethod, TestCategory("mapping"), TestCategory("critical")]
		[Description("Notes played one after another each strum once")]
		public void Consecutive_notes_each_strum()
		{
			var strum = NewStrum();
			var trace = new Trace();
			// Green, red, yellow, blue, orange, 150 ms apart, each held 100 ms.
			var notes = new[] { G, R, Y, B, O };
			Run(strum, trace, 0, 1000, t =>
			{
				var i = t / 150;
				return i < notes.Length && t % 150 < 100 ? notes[i] : 0;
			});
			CollectionAssert.AreEqual(new[] { 10, 160, 310, 460, 610 }, trace.StrumStarts());
		}

		[TestMethod, TestCategory("mapping")]
		[Description("The same key pressed again is a new note")]
		public void The_same_key_again_is_a_new_note()
		{
			var strum = NewStrum();
			var trace = new Trace();
			Run(strum, trace, 0, 400, t => t % 100 < 60 ? R : 0);
			Assert.AreEqual(4, trace.StrumStarts().Count);
		}

		[TestMethod, TestCategory("mapping")]
		[Description("A key pressed while another is held strums, as a note played with a lower fret held")]
		public void A_key_pressed_over_a_held_one_strums()
		{
			var strum = NewStrum();
			var trace = new Trace();
			Run(strum, trace, 0, 400, t => G | (t >= 200 ? R : 0));
			CollectionAssert.AreEqual(new[] { 10, 210 }, trace.StrumStarts());
			Assert.AreEqual(G | R, trace.Frets[210]);
		}

		[TestMethod, TestCategory("mapping"), TestCategory("critical")]
		[Description("In one direction, a note during a strum lets the strum go for the gap, then strums again")]
		public void One_direction_lets_go_between_strums()
		{
			var strum = NewStrum(StrumDirection.Down);
			var trace = new Trace();
			// Red 20 ms after green, while green's strum (10 to 40) is still out.
			Run(strum, trace, 0, 300, t => (t >= 0 && t < 200 ? G : 0) | (t >= 20 && t < 200 ? R : 0));
			var starts = trace.StrumStarts();
			Assert.AreEqual(2, starts.Count, "Two notes, two strums.");
			// Red's window closes at 30: the first strum ends there, and the second waits the 20 ms gap.
			Assert.AreEqual(StrumOutput.None, trace.Strums[30], "The strum is let go so the game sees a new one.");
			Assert.AreEqual(50, starts[1]);
			for (var t = 30; t < 50; t++)
				Assert.AreEqual(StrumOutput.None, trace.Strums[t], "Let go for the whole gap, read " + t + ".");
			Assert.AreEqual(StrumOutput.Down, trace.Strums[50]);
		}

		[TestMethod, TestCategory("mapping")]
		[Description("Alternating goes down, up, down, and a note during a strum turns it the other way at once")]
		public void Alternating_turns_at_once()
		{
			var strum = NewStrum(StrumDirection.Alternate);
			var trace = new Trace();
			Run(strum, trace, 0, 400, t => (t < 300 ? G : 0) | (t >= 20 && t < 300 ? R : 0) | (t >= 200 ? Y : 0));
			var starts = trace.StrumStarts();
			CollectionAssert.AreEqual(new[] { 10, 30, 210 }, starts, "No gap: red's strum goes out as its window closes.");
			Assert.AreEqual(StrumOutput.Down, trace.Strums[10]);
			Assert.AreEqual(StrumOutput.Up, trace.Strums[30]);
			Assert.AreEqual(StrumOutput.Down, trace.Strums[210]);
		}

		[TestMethod, TestCategory("mapping")]
		[Description("Always up strums up")]
		public void Up_strums_up()
		{
			var strum = NewStrum(StrumDirection.Up);
			var trace = new Trace();
			Run(strum, trace, 0, 100, t => B);
			Assert.AreEqual(StrumOutput.Up, trace.Strums[10]);
		}

		[TestMethod, TestCategory("mapping")]
		[Description("Fast notes in one direction still each get a strum the game can see")]
		public void Fast_notes_in_one_direction_each_strum()
		{
			// Sixteenth notes at 200 beats a minute: one every 75 ms, each held 40 ms.
			var strum = NewStrum();
			var trace = new Trace();
			var notes = new[] { G, R, G, R, G, R, G, R };
			Run(strum, trace, 0, 75 * notes.Length + 100, t =>
			{
				var i = t / 75;
				return i < notes.Length && t % 75 < 40 ? notes[i] : 0;
			});
			Assert.AreEqual(notes.Length, trace.StrumStarts().Count);
		}

		#endregion

		#region Switched off and reset

		[TestMethod, TestCategory("mapping")]
		[Description("With the automatic strum off the frets follow the keys and nothing strums")]
		public void Off_the_frets_follow_the_keys()
		{
			var strum = NewStrum();
			strum.Enabled = false;
			var trace = new Trace();
			Run(strum, trace, 0, 200, t => t >= 50 && t < 53 ? G : 0);
			Assert.AreEqual(0, trace.StrumStarts().Count);
			Assert.AreEqual(G, trace.Frets[50]);
			Assert.AreEqual(0, trace.Frets[53], "No latch: the fret lets go with the key.");
		}

		[TestMethod, TestCategory("mapping")]
		[Description("A reset lets go of everything, a strum under way included")]
		public void Reset_lets_go_of_everything()
		{
			var strum = NewStrum();
			strum.Update(0, G | R);
			strum.Update(15, G | R);
			Assert.AreEqual(StrumOutput.Down, strum.Strum);
			strum.Reset();
			Assert.AreEqual(StrumOutput.None, strum.Strum);
			Assert.AreEqual(0, strum.Frets);
			Assert.AreEqual(0, strum.StrumCount);
		}

		#endregion

		#region The device

		/// <summary>A device on a keyboard the test holds keys on, a clock it sets, and the game in front unless it says not.</summary>
		sealed class Rig
		{
			public readonly HashSet<int> Down = new HashSet<int>();
			public double Now;
			public bool InFront = true;
			public readonly GH3Keyboard Device;
			public readonly PluginState State = new PluginState();

			public Rig()
			{
				Device = new GH3Keyboard(key => Down.Contains(key), () => Now, () => InFront);
			}

			public void Read(double now)
			{
				Now = now;
				Device.Read(State);
			}
		}

		[TestMethod, TestCategory("mapping"), TestCategory("critical")]
		[Description("The device has the controls x360ce's own mapping turns into a Guitar Hero III guitar")]
		public void The_device_has_the_controls_of_a_guitar()
		{
			var device = new GH3Keyboard(k => false, () => 0, () => true);
			Assert.AreEqual(8, device.ButtonCount);
			Assert.AreEqual(1, device.PovCount, "The hat is the strum: x360ce's own mapping makes it the D-pad.");
			Assert.AreEqual(0, device.AxisCount);
			Assert.AreEqual(0, device.SliderCount);
			Assert.AreEqual(new Guid("6c1f3a52-8f0e-4b8d-9a35-2f7d4c1e9b60"), device.ProductGuid,
				"Saved mappings find the device by it, so it never changes.");
			// x360ce's own mapping of a new gamepad: button 0 A, 1 B, 2 X, 3 Y, 4 left bumper, 6 Back, 7 Start.
			// Guitar Hero III reads green A, red B, yellow Y, blue X and orange the left bumper.
			CollectionAssert.AreEqual(new[] { 0, 1, 3, 2, 4 }, GH3Keyboard.FretButtons);
			Assert.AreEqual(6, GH3Keyboard.StarPowerButton);
			Assert.AreEqual(7, GH3Keyboard.StartButton);
		}

		[TestMethod, TestCategory("mapping"), TestCategory("critical")]
		[Description("The suggested keys A, S, D, F and G press green, red, yellow, blue and orange, and strum down on the hat")]
		public void The_suggested_keys_play_the_five_notes()
		{
			var keys = new[] { 'A', 'S', 'D', 'F', 'G' };
			for (var i = 0; i < keys.Length; i++)
			{
				var rig = new Rig();
				rig.Down.Add(keys[i]);
				rig.Read(0);
				for (var b = 0; b < 8; b++)
					Assert.AreEqual(b == GH3Keyboard.FretButtons[i], rig.State.Buttons[b], "Key " + keys[i] + ", button " + b + ".");
				Assert.AreEqual(PluginState.PovReleased, rig.State.Povs[0], "No strum before the window closes.");
				rig.Read(10);
				Assert.AreEqual(GH3Keyboard.HatDown, rig.State.Povs[0], "Key " + keys[i] + " strums down.");
				rig.Read(40);
				Assert.AreEqual(PluginState.PovReleased, rig.State.Povs[0], "The strum lets go.");
				Assert.IsTrue(rig.State.Buttons[GH3Keyboard.FretButtons[i]], "The fret stays while the key is held.");
			}
		}

		[TestMethod, TestCategory("mapping")]
		[Description("A note given another key answers to it, and no longer to the old one")]
		public void A_note_can_take_any_key()
		{
			var rig = new Rig();
			rig.Device.GreenKey = GH3Key.J;
			rig.Down.Add('A');
			rig.Read(0);
			Assert.IsFalse(rig.State.Buttons[0], "A is not green any more.");
			rig.Down.Clear();
			rig.Down.Add('J');
			rig.Read(1);
			Assert.IsTrue(rig.State.Buttons[0], "J is.");
		}

		[TestMethod, TestCategory("mapping"), TestCategory("critical")]
		[Description("With the game in the back nothing is read, and what was held is let go")]
		public void With_the_game_in_the_back_everything_is_let_go()
		{
			var rig = new Rig();
			rig.Down.Add('A');
			rig.Down.Add('S');
			rig.Read(0);
			rig.Read(12);
			Assert.AreEqual(GH3Keyboard.HatDown, rig.State.Povs[0]);
			rig.InFront = false;
			rig.Read(13);
			Assert.IsFalse(rig.Device.Active);
			for (var b = 0; b < 8; b++)
				Assert.IsFalse(rig.State.Buttons[b], "Button " + b + " is let go.");
			Assert.AreEqual(PluginState.PovReleased, rig.State.Povs[0], "The strum is let go, mid pulse.");
			rig.Read(500);
			Assert.AreEqual(PluginState.PovReleased, rig.State.Povs[0]);
		}

		[TestMethod, TestCategory("mapping")]
		[Description("Read anywhere when told to, without the game in front")]
		public void Reads_anywhere_when_told()
		{
			var rig = new Rig { InFront = false };
			rig.Device.OnlyWhenGameInFront = false;
			rig.Down.Add('D');
			rig.Read(0);
			Assert.IsTrue(rig.Device.Active);
			Assert.IsTrue(rig.State.Buttons[3], "Yellow is button 3, Y.");
		}

		[TestMethod, TestCategory("mapping")]
		[Description("The keys for Star Power, Start and a strum by hand do what they say")]
		public void The_other_keys_work()
		{
			var rig = new Rig();
			rig.Device.StarPowerKey = GH3Key.Space;
			rig.Device.StartKey = GH3Key.Enter;
			rig.Device.StrumUpKey = GH3Key.Up;
			rig.Device.StrumDownKey = GH3Key.Down;
			rig.Down.Add(0x20);
			rig.Down.Add(0x0D);
			rig.Down.Add(0x26);
			rig.Read(0);
			Assert.IsTrue(rig.State.Buttons[GH3Keyboard.StarPowerButton]);
			Assert.IsTrue(rig.State.Buttons[GH3Keyboard.StartButton]);
			Assert.AreEqual(GH3Keyboard.HatUp, rig.State.Povs[0], "A strum up by hand, as the menus need.");
			rig.Down.Remove(0x26);
			rig.Down.Add(0x28);
			rig.Read(1);
			Assert.AreEqual(GH3Keyboard.HatDown, rig.State.Povs[0]);
		}

		[TestMethod, TestCategory("mapping")]
		[Description("A key given to two functions is named in the warning")]
		public void A_key_used_twice_is_warned_about()
		{
			var device = new GH3Keyboard(k => false, () => 0, () => true);
			Assert.AreEqual("", device.Warning);
			device.RedKey = GH3Key.A;
			StringAssert.Contains(device.Warning, "A");
		}

		[TestMethod, TestCategory("mapping")]
		[Description("The plugin is embedded in x360ce and loads with it")]
		public void The_plugin_is_embedded_in_x360ce()
		{
			var type = TestControllerDevice.Manager.FindDeviceType(GH3Keyboard.Product);
			Assert.IsNotNull(type, "The GH3 Keyboard plugin is not embedded in x360ce.exe.");
			Assert.IsFalse(type.Faulted, type.FaultReason);
			Assert.AreEqual("GH3 Keyboard", type.Name);
			Assert.AreEqual(8, type.ButtonCount);
			Assert.AreEqual(1, type.PovCount);
		}

		#endregion
	}
}
