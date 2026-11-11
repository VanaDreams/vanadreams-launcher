return {
    ['Provoke whatever you have targeted.'] = '現在のターゲットに挑発を使用します。',
    ['Cast Haste on yourself, wait 4 seconds, then Refresh.'] = '自分にヘイストを唱え、4 秒待ってリフレシュを唱えます。',
    ['Put on your Warp Ring, use it and give the slot back (any job).'] = 'デジョンリングを装備・使用し、元の部位に戻します（全ジョブ）。',
    ['BST: wear your set called lizard (the jug) and use Bestial Loyalty.'] = '獣使い：lizard セット（汁ペット）を装備して Bestial Loyalty（しょうしゅう）を使用します。',
    ['BST skillchain: each step goes when the one before has landed; the burst goes at once.'] = '獣使いの連携：直前の行動の完了後に次へ進み、バーストは即座に実行します。',
    ['Two weaponskills, the second as soon as the first has finished and the link gap has passed.'] = 'WS を 2 回実行します。最初の WS の完了と連携の待機時間を確認して次を実行します。',
    ['How to: your commands, chains and going further'] = '使い方：マイコマンド・連携・応用',
    ['Everything here is done in the Your commands tab, which writes your commands into your job\'s profile '
        .. '(config\\addons\\vanagear\\profiles\\<Name>_<id>\\<JOB>.lua, its commands table; ALL.lua beside it for every job). '
        .. 'You never need Lua for a chain. Every /vg command is listed in the Your sets tab, under Commands.'] =
        '「マイコマンド」タブで設定します。ジョブのプロファイル（config\\addons\\vanagear\\profiles\\<Name>_<id>\\<JOB>.lua）の commands テーブルに保存されます。全ジョブ共通は同じ場所の ALL.lua です。連携の作成に Lua の知識は不要です。/vg コマンドは「装備セット」タブのコマンド欄に表示されます。',
    ['1. A command word'] = '1. 実行用の単語',
    ['A word you choose runs its lines in order. Any game command is a line: /ja "Provoke" <t>, '
        .. '/ma "Cure IV" <stpc>, /ws "Savage Blade" <t>, /pet "Tail Blow" <t>, /vg wear Idle. Type /vg <word> or just /<word>.'] =
        '選んだ単語で各行を順に実行します。/ja、/ma、/ws、/pet、/vg wear Idle などを記述できます。/vg <word> または /<word> で実行します。魔法・技の名前にはクライアントで使える名称を指定してください。',
    ['wait 3          pause three seconds before the next line'] = 'wait 3          次の行まで 3 秒待つ',
    ['/vg with <set> <command>   put one of your sets on, send the command, keep the set on until it lands'] = '/vg with <set> <command>   指定セットを装備し、その行動が完了するまで維持',
    ['/vg wear <set>  put one of your sets on and leave it on (/vg wear off)'] = '/vg wear <set>  指定セットを維持（/vg wear off で解除）',
    ['2. A chain: steps that wait for each other'] = '2. 連携：行動の完了を待つ手順',
    ['A line that starts with "then" does not go until the action on the line before it has been seen to '
        .. 'start and then finish, plus the gap the server wants between links (3.3 seconds). A line that starts with '
        .. '"burst" goes the moment it finishes, with no gap. That is how a skillchain is timed without guessing waits.'] =
        'then で始まる行は、直前の行動の開始と完了を確認し、連携間隔（3.3 秒）を待ってから実行します。burst で始まる行は完了直後に実行します。待機時間を推測せずに連携を組めます。',
    ['Brain Crush, then Tail Blow once it has landed (Fusion), then Cloudsplitter once that has landed '
        .. '(Light), then Fireball the moment Cloudsplitter finishes (the burst). The lines "then" can wait on are '
        .. '/ws, /ja, /ma and /pet. If a step never starts (no TP, no charges, out of range) or the target dies or '
        .. 'changes, the command stops and says why. /vg stop stops one by hand.'] =
        'Brain Crush → Tail Blow（核熱）→ Cloudsplitter（光）→ 完了直後の Fireball（バースト）の例です。then は /ws、/ja、/ma、/pet の完了を待てます。TP・チャージ不足や射程外で開始できない場合、または敵の死亡・ターゲット変更時は理由を表示して停止します。/vg stop で手動停止できます。',
    ['What it does not do: it does not check TP, Ready charges or finishing moves before it starts. '
        .. 'Start it when you have them.'] = '開始前の TP、しじをさせろのチャージ、フィニシングムーブの確認は行いません。必要な量を確保してから実行してください。',
    ['3. Your own sets in a command'] = '3. コマンドで自作セットを使う',
    ['Make a set in the Your sets tab (or /vg save <name> from what you wear), then "/vg with <name> <command>" '
        .. 'wears it for that one action. For Beastmaster: /vg call <jug> and /vg loyal <jug> put the jug in the ammo '
        .. 'slot and call it; /vg jugs lists the ones you carry.'] =
        '「装備セット」タブまたは /vg save <name> で保存し、/vg with <name> <command> でその行動中だけ装備します。獣使いの /vg call <jug> と /vg loyal <jug> は汁を矢弾に装備して呼び出します。/vg jugs で所持している汁を表示します。',
    ['4. Going further: your profile in Lua'] = '4. 応用：Lua プロファイル',
    ['Each job runs a profile file at config\\addons\\vanagear\\profiles\\<Name>_<id>\\<JOB>.lua (/vg edit opens it). '
        .. 'It holds the built sets (rewritten at each rebuild), your sets (what the window and /vg addset write), your commands, '
        .. 'and these functions, each called at that moment:'] =
        'ジョブごとのプロファイルは config\\addons\\vanagear\\profiles\\<Name>_<id>\\<JOB>.lua です（/vg edit で開く）。自動生成セット、自作セット、コマンドと、以下のタイミングで呼ばれる関数を保持します。',
    ['OnLoad / OnUnload          when the profile starts and stops'] = 'OnLoad / OnUnload          プロファイルの開始・終了時',
    ['HandleDefault              every time nothing is in flight: idle, engaged, resting; chains tick here'] = 'HandleDefault              行動していない時（待機・戦闘・ヒーリング）。連携もここで進行',
    ['HandleCommand(args)        /vg fwd <word> ... reaches here: your own commands in Lua'] = 'HandleCommand(args)        /vg fwd <word> ... で呼び出す自作 Lua コマンド',
    ['HandlePrecast / HandleMidcast   a spell: gear before it starts, gear while it casts'] = 'HandlePrecast / HandleMidcast   魔法の詠唱前・詠唱中の装備',
    ['HandleWeaponskill / HandleAbility / HandleItem   a weaponskill, job ability or item, as it goes off'] = 'HandleWeaponskill / HandleAbility / HandleItem   WS・アビリティ・アイテムの実行時',
    ['HandlePreshot / HandleMidshot   a ranged attack, before and during'] = 'HandlePreshot / HandleMidshot   遠隔攻撃の開始前・実行中',
    ['Inside them: gFunc.Equip(slot, item), gFunc.EquipSet(set), gFunc.ForceEquip, gFunc.LockSet, '
        .. 'gFunc.Disable/Enable(slot), gFunc.Message(text); and gData.GetPlayer() (job, level, TP, HPP), '
        .. 'gData.GetTarget(), gData.GetPet(), gData.GetAction() and gData.GetPetAction() (what is going off right now, '
        .. 'nil when nothing is), gData.GetBuffCount(id), gData.GetEquipment(), gData.GetAugment(). '
        .. 'A chain in Lua is a step list, a send, then watching GetAction until it comes back nil before the next send; '
        .. 'the "then" line above is exactly that, done for you.'] =
        '装備操作は gFunc.Equip、EquipSet、ForceEquip、LockSet、Disable/Enable、表示は gFunc.Message を使います。gData.GetPlayer（ジョブ・レベル・TP・HPP）、GetTarget、GetPet、GetAction、GetPetAction、GetBuffCount、GetEquipment、GetAugment で状態を取得できます。行動がない時の GetAction は nil です。then は、行動の完了確認と次の送信までの待機を代行します。',
    ['AshitaCore:GetChatManager():QueueCommand(1, "/ws \\"Savage Blade\\" <t>") sends any command from Lua.'] = 'AshitaCore:GetChatManager():QueueCommand(1, command) で Lua からゲームのコマンドを送信できます。',
    ['Make your own short command. You pick a word, then write the game commands it runs, one per line. '
        .. 'Afterwards typing /vg <word> (or just /<word>) runs them for you.'] =
        '実行用の単語を選び、各行にゲームのコマンドをひとつずつ記述します。保存後は /vg <word> または /<word> で実行できます。',
    ['Any game command works: /ja "Provoke" <t>, /ma "Cure IV" <stpc>, /vg wear Idle. '
        .. 'A line "wait 3" waits 3 seconds before the next line. <t> is your target, <me> is you.'] =
        '/ja、/ma、/vg wear Idle などのコマンドが使えます。wait 3 は次の行まで 3 秒待ちます。<t> はターゲット、<me> は自分です。技・魔法の名称にはクライアントで使える名前を指定してください。',
    ['A line "then /ws ..." goes once the action before it has finished (plus the link gap); '
        .. '"burst /pet ..." goes the moment it finishes. The How to tab explains chains.'] =
        'then /ws ... は直前の行動完了と連携間隔を待ちます。burst /pet ... は完了直後に実行します。詳しくは「使い方」タブをご覧ください。',
    ['open the window: your sets, the piece in each slot and why'] = '装備セット・各部位の装備と選定結果を表示',
    ['rebuild the sets now (also happens on job change, level up and new gear)'] = 'セットを再作成（ジョブ変更・レベルアップ・装備入手時にも自動実行）',
    ['what is running, and any piece your sets name that you do not carry'] = '動作状態と、セット内で未所持の装備を確認',
    ['damage taken gear over everything else: physical, magic or both (also in the Settings tab)'] = '物理・魔法・両方の被ダメージ軽減装備を優先（設定タブでも変更可）',
    ['movement gear over everything else, on or off'] = '移動速度装備の優先を切り替え',
    ['treasure hunter gear over everything else, on or off'] = 'トレジャーハンター装備の優先を切り替え',
    ['which fighting set you wear (proc: your low-damage Tp_Proc and Ws_Proc)'] = '戦闘セットを選択（proc は低ダメージ用 Tp_Proc / Ws_Proc）',
    ['standing still: auto puts regen on below 60% HP and refresh below 70% MP'] = '待機装備。auto は HP 60% 未満でリジェネ、MP 70% 未満でリフレシュを使用',
    ['standing still: auto puts regen on below 60% HP'] = '待機装備。auto は HP 60% 未満でリジェネを使用',
    ['damage or accuracy for nukes and enfeebles'] = '精霊・弱体魔法の威力または命中を優先',
    ['magic burst gear on nukes, on or off'] = '精霊魔法のマジックバースト装備を切り替え',
    ['precast, then safety gear while casting, then midcast gear just before the spell lands'] = '詠唱前→詠唱中の防御装備→発動直前の魔法装備を使用',
    ['what your gear does for your pet while it fights'] = 'ペット戦闘中の装備方針を選択',
    ['put the jug in your ammo slot and Call Beast (no jug: the last one you called)'] = '汁を矢弾に装備してよびだす（省略時は前回の汁）',
    ['the same with Bestial Loyalty, which keeps the jug'] = 'しょうしゅうで呼び出す（汁を消費しません）',
    ['put the pet food in and Reward: alpha ... theta, debuff (Roborant, else Poultice); no food: your best biscuit'] = 'ペットフードを装備していたわる。alpha～theta、debuff（ロボラント、なければポウルティス）。省略時は最良のビスケット',
    ['the jugs and pet foods you carry'] = '所持している汁とペットフードを表示',
    ['allow Main/Sub swaps from your own profile (costs TP); weapons/ammo are never generated'] = '自分のプロファイルによるメイン・サブ武器変更を許可（TP 消失）。武器・矢弾は自動選定しません',
    ['Main/Sub: your pick in every set. Range: equip once; use /vg set for a saved action choice'] = 'メイン・サブは全セット共通。遠隔は一度だけ装備。行動用に保存する場合は /vg set',
    ['keep a slot as you have it: nothing equips it, saved for this job (/vg disable and /vg enable are the same)'] = '現在の装備で部位を固定し、ジョブごとに保存（disable / enable も同じ動作）',
    ['the piece that won and the three behind it'] = '選定された装備と次点の 3 個を表示',
    ['pieces in storage that would win a slot'] = '収納内の装備から有効な更新候補を表示',
    ['fill the slots your sets leave empty from the built sets (off: your sets exactly as written)'] = '自作セットの空き部位を自動生成で補完（off は記述通りに使用）',
    ['read your profile again / load another / make a new one, in config\\addons\\vanagear\\profiles'] = 'プロファイルの再読込・別ファイルの読込・新規作成（config\\addons\\vanagear\\profiles）',
    ["write this job's profile again from its job template, keeping your sets and commands (the old one goes to backups first)"] = 'セットとコマンドを保持してジョブテンプレートから再作成（先にバックアップ）',
    ['open your profile in your editor / list every set worn, built ones included (/vg sets lists only yours)'] = 'プロファイルを編集・自動生成を含むセット一覧を表示（/vg sets は自作のみ）',
    ['keep a set on for a while / put one piece on'] = '一定時間セットを維持・装備を 1 個装着',
    ["hand a command to your profile's own HandleCommand"] = 'プロファイルの HandleCommand にコマンドを渡す',
    ["the engine's look lock and strip"] = '外見の固定・装備をすべて外す',
    ['Packer: fetch, check and put away the gear your profile names'] = 'Packer：プロファイルの装備を取り出す・確認する・収納する',
    ["the engine's settings window, debug output, run a line of Lua"] = '詳細設定・デバッグ表示・Lua の実行',
    ['give your called trusts the stats of a set from your bags (a tab each in /vg); off stops sending'] = '所持品の装備性能をフェイスに適用（/vg に個別タブ）。off で送信停止',
    ['equip %s, wait it out, use it, give the slot back'] = '%s を装備し、使用可能になるまで待って使用後に戻す',
    ['yours: %s'] = '自作：%s',
    ['make your own command for this job, run with /vg <word> or just /<word> (add "all" before the word for every job); "wait 2" pauses'] = 'このジョブ用のコマンドを作成。/vg <word> または /<word> で実行。単語の前に all を付けると全ジョブ共通。wait 2 は 2 秒待機',
    ['in a command: goes once the action before it has finished, plus the link gap / at once (a chain); /vg stop stops a running command'] = '直前の行動完了後、連携間隔を待つ／即座に実行。/vg stop で停止',
    ['put a set on, then the command (/ja, /ma, /pet, /ws), and keep the set on until it is done'] = 'セットを装備して /ja・/ma・/pet・/ws を実行し、完了まで維持',
    ['stop swapping so you can put on what you like from the equipment menu'] = '装備画面から自由に着替えるため、自動着替えを停止',
    ['save what you are wearing as a set (swapping comes back on). Named like a built set (Idle, Tp_Default, Reward, a weaponskill) it replaces those slots there'] = '現在の装備を保存して着替えを再開。Idle・Tp_Default・Reward・WS 名などの既存セット名なら、その部位を上書き',
    ['change one slot of a set (any set in the /vg window too); /vg sets lists yours'] = 'セットの部位を変更（画面でも操作可）。/vg sets で自作セットを表示',
    ['put on one of your sets and keep it on over the built sets'] = '自作セットを装備し、自動生成より優先して維持',
    ['lock a set as your look (/lockstyle), built or yours, kept per job; the swaps go on underneath without showing'] = '自作・自動生成セットの外見をジョブごとに固定。内部の着替えは継続',
    ['delete one of your commands or sets; open your profiles folder to edit by hand (then /vg reload)'] = '自作コマンド・セットを削除。プロファイルフォルダーを開く（編集後は /vg reload）',
    ['how long after zoning in before gear changes (protects the server)'] = 'エリア移動後、着替え開始までの待機秒数（サーバー保護）',
    ['the same as /vg <word>, so GearSwap macros keep working'] = '/vg <word> と同じ。GearSwap のマクロを継続使用',
    ['put on a set by its GearSwap or vanagear name'] = 'GearSwap または VanaGear のセット名で装備',
    ['choose Auto, English or Japanese for the addon display'] = 'アドオンの表示言語を自動・英語・日本語から選択',
};
