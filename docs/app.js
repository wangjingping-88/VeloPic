const features = {
  library: {
    index: "01",
    title: "图库浏览",
    description: "虚拟化网格按需加载缩略图，平铺模式全局浏览，按目录模式可逐组折叠。"
  },
  media: {
    index: "02",
    title: "图片与视频",
    description: "一次递归扫描图片和视频，在同一工作台切换媒体类型与平铺、按目录浏览模式。"
  },
  filter: {
    index: "03",
    title: "组合筛选",
    description: "在一个筛选面板中组合文件名、分辨率和类型，结果随条件变化快速更新。"
  },
  classify: {
    index: "04",
    title: "智能分类",
    description: "本地 ONNX 模型辅助识别风景、城市、人物与自然，不上传原始图片。"
  },
  wallpaper: {
    index: "05",
    title: "壁纸整理",
    description: "加入壁纸收藏，预览填充、适中和居中效果，确认后再应用到 Windows 桌面。"
  }
};

const stage = document.querySelector(".product-stage");
const featureTitle = document.querySelector("#feature-title");
const featureDescription = document.querySelector("#feature-description");
const captionIndex = document.querySelector(".caption-index");

document.querySelectorAll(".feature-tab").forEach((button) => {
  button.addEventListener("click", () => {
    const feature = button.dataset.feature;
    const content = features[feature];
    if (!content) return;

    document.querySelectorAll(".feature-tab").forEach((item) => {
      const selected = item === button;
      item.classList.toggle("is-active", selected);
      item.setAttribute("aria-pressed", String(selected));
    });

    stage.dataset.feature = feature;
    captionIndex.textContent = content.index;
    featureTitle.textContent = content.title;
    featureDescription.textContent = content.description;
  });
});

const root = document.documentElement;
const themeToggle = document.querySelector(".theme-toggle");
const storedTheme = localStorage.getItem("velopic-site-theme");
const preferredDark = window.matchMedia("(prefers-color-scheme: dark)").matches;

function applyTheme(theme) {
  root.dataset.theme = theme;
  themeToggle.querySelector("span").textContent = theme === "dark" ? "☾" : "☼";
}

applyTheme(storedTheme || (preferredDark ? "dark" : "light"));

themeToggle.addEventListener("click", () => {
  const nextTheme = root.dataset.theme === "dark" ? "light" : "dark";
  applyTheme(nextTheme);
  localStorage.setItem("velopic-site-theme", nextTheme);
});

const observer = new IntersectionObserver((entries) => {
  entries.forEach((entry) => {
    if (entry.isIntersecting) {
      entry.target.classList.add("is-visible");
      observer.unobserve(entry.target);
    }
  });
}, { threshold: 0.12 });

document.querySelectorAll(".reveal").forEach((element) => observer.observe(element));
