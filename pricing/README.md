# 独立价格表

`catalog-v1.json` 是维护者核对官方 API 价格后的完整快照，不是 OpenAI 的自动报价接口。

在 GitHub 的 `main` 分支编辑 [catalog-v1.json](https://github.com/yaozhihang2002/CodexQuotaPanel/edit/main/pricing/catalog-v1.json)，提交后用户点击“同步费率”即可获取；默认也会每天静默同步，无通知。不需要发布安装包或更改应用版本号。

更新流程：核对 [OpenAI 官方价格](https://developers.openai.com/api/docs/pricing)，修改对应模型及其 `basisDate`、`checkedAt`（填写真实核对日期）。同一 `revision` 的内容修改也会生效；发布新代快照时可递增 `revision`。新模型提供独立显示名和计价参数，不需重新编译客户端。保留历史模型以便重算已有记录；不要把未核实的模型标成已定价。提交前建议运行 Domain 和 Infrastructure 测试。

客户端无需用户登录 GitHub：优先 GitHub Contents API，网络故障或限流时尝试 raw 文件地址。只读取固定 HTTPS 地址，限制为 128 KiB，验证结构、唯一模型名、费率及倍数，拒绝 revision 或核对日期回退。下载、解析或保存失败继续沿用本地表；没有缓存时使用程序内置快照。自动同步默认开启，最多每 24 小时检查一次，成功和失败都不通知；设置中可关闭，也可手动同步并查看结果。

金额是按当前快照计算的 API 等价估算，不是历史账单。`Auto-review` 仍使用历史映射并保留独立核对日期。
